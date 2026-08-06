using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TourMonitor.Notifications;

/// <summary>Отправка сообщений в Telegram через Bot API (прямой HTTP, без сторонних пакетов).</summary>
public sealed class TelegramNotifier
{
    private readonly HttpClient _http;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramNotifier> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.BotToken) && !string.IsNullOrWhiteSpace(_options.ChatId);

    /// <summary>Отправляет несколько сообщений по очереди; возвращает число отправленных.</summary>
    public async Task<int> SendMessagesAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Telegram не настроен (BotToken/ChatId) — сообщения не отправлены.");
            return 0;
        }

        var sent = 0;
        foreach (var text in messages)
        {
            if (await SendMessageAsync(text, ct))
                sent++;
        }
        return sent;
    }

    public async Task<bool> SendMessageAsync(string text, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Telegram не настроен (BotToken/ChatId) — сообщение не отправлено.");
            return false;
        }

        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = _options.ChatId!,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true,
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
                using var response = await _http.PostAsJsonAsync(url, payload, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return true;

                _logger.LogWarning("Telegram вернул {(StatusCode)}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Telegram недоступен ({Message})", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return false;
    }
}
