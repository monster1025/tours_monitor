using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace TourMonitor.Camoufox;

/// <summary>
/// Долгоживущая сессия Camoufox: один браузер на весь скан. Лениво запускается,
/// открывает level.travel для установки кук и реального браузерного контекста,
/// дальше API-запросы идут из этой же страницы.
/// </summary>
public sealed class CamoufoxBrowserSession : IAsyncDisposable
{
    private readonly ICamoufoxInstaller _installer;
    private readonly CamoufoxOptions _options;
    private readonly ILogger<CamoufoxBrowserSession> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private bool _started;

    public CamoufoxBrowserSession(ICamoufoxInstaller installer, IOptions<CamoufoxOptions> options, ILogger<CamoufoxBrowserSession> logger)
    {
        _installer = installer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IPage> GetPageAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_started && _page is not null)
                return _page;

            var executablePath = await _installer.EnsureInstalledAsync(
                _options.Version, _options.InstallDirectory, _options.DownloadUrlOverride, cancellationToken);
            _logger.LogInformation("Запуск браузерной сессии Camoufox: {ExecutablePath}", executablePath);

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
            {
                ExecutablePath = executablePath,
                Headless = _options.Headless,
            });

            _page = await _browser.NewPageAsync();
            _logger.LogInformation("Открытие {SessionUrl} для установки сессии...", _options.SessionUrl);
            await _page.GotoAsync(_options.SessionUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });

            _started = true;
            _logger.LogInformation("Браузерная сессия готова, текущий URL: {Url}", _page.Url);
            return _page;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_page is not null)
            {
                try { await _page.CloseAsync(); } catch { /* ignore */ }
                _page = null;
            }
            if (_browser is not null)
            {
                try { await _browser.CloseAsync(); } catch { /* ignore */ }
                _browser = null;
            }
            if (_playwright is not null)
            {
                _playwright.Dispose();
                _playwright = null;
            }
            _started = false;
            _logger.LogInformation("Браузерная сессия Camoufox закрыта.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();
}
