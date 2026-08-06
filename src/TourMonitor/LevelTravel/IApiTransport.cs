namespace TourMonitor.LevelTravel;

/// <summary>Сырой ответ API (статус + тело) до расшифровки/дешифровки.</summary>
public sealed class ApiTransportResponse
{
    public int Status { get; set; }
    public string Body { get; set; } = "";
}

/// <summary>
/// Транспорт запросов к API. Единственная реализация — реальный браузер Camoufox
/// (CamoufoxApiTransport): fetch из контекста страницы level.travel обходит анти-бот периметр.
/// </summary>
public interface IApiTransport
{
    Task<ApiTransportResponse> SendAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct = default);
}
