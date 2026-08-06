using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using TourMonitor.Camoufox;

namespace TourMonitor.LevelTravel;

/// <summary>
/// Транспорт через реальный браузер Camoufox: fetch выполняется из контекста страницы
/// level.travel, что даёт настоящий браузерный фингерпринт, куки и Origin.
/// </summary>
public sealed class CamoufoxApiTransport : IApiTransport
{
    private const string FetchScript = """
        async ({ method, url, headers, body }) => {
            const res = await fetch(url, {
                method,
                headers,
                body: body === undefined || body === null ? undefined : body,
            });
            const text = await res.text();
            return { status: res.status, body: text };
        }
        """;

    private readonly CamoufoxBrowserSession _session;
    private readonly LevelTravelOptions _options;
    private readonly ILogger<CamoufoxApiTransport> _logger;

    public CamoufoxApiTransport(
        CamoufoxBrowserSession session,
        IOptions<LevelTravelOptions> options,
        ILogger<CamoufoxApiTransport> logger)
    {
        _session = session;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApiTransportResponse> SendAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct = default)
    {
        var page = await _session.GetPageAsync(ct);

        var headers = new Dictionary<string, string>
        {
            ["X-Cnt"] = _options.Country,
            ["X-Lang"] = _options.Language,
            ["X-Cur"] = _options.Currency,
            ["Accept-Language"] = $"{_options.Language}-{_options.Country.ToUpperInvariant()},{_options.Language};q=0.9",
            ["Accept"] = $"application/vnd.leveltravel.v{_options.ApiVersion}",
            ["Authorization"] = $"Token token=\"{_options.ApiKey}\"",
            ["Content-Type"] = "application/json",
        };

        var response = await page.EvaluateAsync<ApiTransportResponse>(FetchScript, new
        {
            method = method.Method.ToUpperInvariant(),
            url,
            headers,
            body = jsonBody,
        });
        return response;
    }
}
