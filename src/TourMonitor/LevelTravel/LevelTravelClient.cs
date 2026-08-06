using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TourMonitor.LevelTravel;

/// <summary>
/// Клиент публичного API Level.Travel (подпись запросов + расшифровка ответов воспроизведены с веб-клиента).
/// Транспорт (CamoufoxApiTransport) инжектируется отдельно.
/// </summary>
public sealed class LevelTravelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IApiTransport _transport;
    private readonly LevelTravelOptions _options;
    private readonly ILogger<LevelTravelClient> _logger;

    public LevelTravelClient(IApiTransport transport, IOptions<LevelTravelOptions> options, ILogger<LevelTravelClient> logger)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MultiEnqueueResponse> MultiEnqueueAsync(IReadOnlyList<SearchParam> searchParams, CancellationToken ct = default)
    {
        var body = new MultiEnqueueRequest { SearchParams = searchParams.ToList() };
        return await PostAsync<MultiEnqueueResponse>("/search/multi_enqueue", body, ct);
    }

    public async Task<SearchStatus> GetStatusAsync(string requestId, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["request_id"] = requestId,
            ["show_size"] = "true",
        };
        return await GetAsync<SearchStatus>("/search/status", parameters, ct);
    }

    public async Task<RoomRatesResponse> GetRoomRatesAsync(string hotelId, string requestId, CancellationToken ct = default)
    {
        var body = new RoomRatesRequest
        {
            HotelId = hotelId,
            RequestId = requestId,
            Filters = new RoomRatesFilters(),
        };
        return await PostAsync<RoomRatesResponse>("/hotel_search/room_rates", body, ct);
    }

    /// <summary>Ожидает завершения поиска: completeness = 100 или все операторы в терминальном статусе.</summary>
    public async Task<SearchStatus> WaitForSearchAsync(string requestId, int pollIntervalSeconds, int timeoutSeconds, CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var terminal = new HashSet<string> { "cached", "completed", "all_filtered", "no_results", "failed", "skipped" };

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var status = await GetStatusAsync(requestId, ct);

            var statuses = status.Status?.Values.ToList() ?? new List<string>();
            var allTerminal = statuses.Count > 0 && statuses.All(s => terminal.Contains(s));
            if (status.Completeness >= 100 || allTerminal)
                return status;

            if (DateTime.UtcNow - startedAt > timeout)
                throw new TimeoutException($"Поиск {requestId} не завершился за {timeoutSeconds} с (completeness={status.Completeness}).");

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds)), ct);
        }
    }

    private async Task<T> GetAsync<T>(string path, IDictionary<string, object?> parameters, CancellationToken ct)
    {
        var sign = SignHelper.ComputeGet(path, parameters, _options.ApiKey, _options.SignSalt, _options.ApiVersion);

        var query = new List<string>();
        foreach (var (key, value) in parameters)
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value?.ToString() ?? "")}");
        query.Add($"key={_options.ApiKey}");
        query.Add($"api_version={_options.ApiVersion}");
        query.Add("js=true");
        query.Add($"sign={sign}");

        var url = $"{_options.BaseUrl}{path}?{string.Join("&", query)}";
        return await ExecuteWithRetryAsync(() => SendAndDeserializeAsync<T>(HttpMethod.Get, url, null, ct), $"GET {path}");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var sign = SignHelper.Compute(path, GetSignValues(body), _options.ApiKey, _options.SignSalt);
        var payload = JsonSerializer.Serialize(body, JsonOptions);
        // sign добавляется последним полем, как это делает веб-клиент
        var payloadWithSign = payload[..^1] + $",\"sign\":\"{sign}\"}}";
        var url = $"{_options.BaseUrl}{path}";
        return await ExecuteWithRetryAsync(() => SendAndDeserializeAsync<T>(HttpMethod.Post, url, payloadWithSign, ct), $"POST {path}");
    }

    internal static IEnumerable<object?> GetSignValues(object body)
    {
        // значения берутся в порядке сериализации (snake_case), порядок для подписи не важен — она сортируется
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body, JsonOptions));
        return doc.RootElement.EnumerateObject().Select(p => GetValue(p.Value)).ToList();
    }

    private static object? GetValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(GetValue).ToList(),
        JsonValueKind.Object => el.EnumerateObject().Select(p => GetValue(p.Value)).ToList(),
        _ => null,
    };

    private async Task<T> SendAndDeserializeAsync<T>(HttpMethod method, string url, string? payload, CancellationToken ct)
    {
        var response = await _transport.SendAsync(method, url, payload, ct);
        var body = response.Body;

        if (response.Status is (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.Unauthorized)
            throw new LevelTravelException($"API отклонило запрос ({response.Status}): {Truncate(body)}");
        if (response.Status is < 200 or >= 300)
            throw new LevelTravelException($"API вернуло {response.Status}: {Truncate(body)}");
        // decoy-ответы анти-бота: 200 с телом {"message":"partner not found"} вместо данных
        if (body.StartsWith("{\"message\":\"partner not found\"", StringComparison.Ordinal))
            throw new LevelTravelException($"API вернуло decoy: {Truncate(body)}");

        return Deserialize<T>(LevelTravelCrypto.DecryptToJson(body, _options.SecretBoxKeys));
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string what)
    {
        var attempts = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };
        Exception? last = null;
        foreach (var delay in attempts)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (ex is LevelTravelException or HttpRequestException or TaskCanceledException)
            {
                last = ex;
                _logger.LogWarning("{What} не удалось ({Message}), повтор через {Delay}s", what, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
        throw new LevelTravelException($"{what} не выполнено после {attempts.Length} попыток.", last!);
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
        }
        catch (JsonException ex)
        {
            throw new LevelTravelException($"Не удалось разобрать ответ: {Truncate(json, 300)}", ex);
        }
    }

    private static string Truncate(string s, int length = 500) =>
        s.Length <= length ? s : s[..length] + "...";
}
