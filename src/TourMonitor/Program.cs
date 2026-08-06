using Hangfire;
using Hangfire.Dashboard;
using Hangfire.InMemory;
using Microsoft.Extensions.Options;
using TourMonitor;
using TourMonitor.Camoufox;
using TourMonitor.Jobs;
using TourMonitor.LevelTravel;
using TourMonitor.Notifications;
using TourMonitor.Storage;
using TourMonitor.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LevelTravelOptions>(builder.Configuration.GetSection("LevelTravel"));
builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("Monitor"));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection("Schedule"));
builder.Services.Configure<CamoufoxOptions>(builder.Configuration.GetSection("Camoufox"));

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage());
builder.Services.AddHangfireServer();

builder.Services.AddSingleton<ICamoufoxReleaseResolver, CamoufoxReleaseResolver>();
builder.Services.AddSingleton<ICamoufoxInstaller, CamoufoxInstaller>();
builder.Services.AddSingleton<CamoufoxBrowserSession>();
builder.Services.AddSingleton<IApiTransport, CamoufoxApiTransport>();
builder.Services.AddSingleton<LevelTravelClient>();
builder.Services.AddHttpClient<TelegramNotifier>(client => client.Timeout = TimeSpan.FromSeconds(20));

builder.Services.AddSingleton(sp =>
{
    var monitor = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
    var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
    var dbPath = Path.IsPathRooted(monitor.DbPath)
        ? monitor.DbPath
        : Path.Combine(contentRoot, monitor.DbPath);
    return new PriceStore(dbPath, sp.GetService<ILogger<PriceStore>>());
});
builder.Services.AddSingleton<DailyScanJob>();

var app = builder.Build();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // По умолчанию Hangfire отдаёт дашборд только с localhost (401 с других хостов);
    // мониторинг должен быть доступен по LAN, поэтому разрешаем все источники.
    Authorization = new[] { new AllowAllDashboardAuthorizationFilter() },
});

var schedule = app.Services.GetRequiredService<IOptions<ScheduleOptions>>().Value;
var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
RecurringJob.AddOrUpdate<DailyScanJob>(
    "daily-scan",
    job => job.ExecuteAsync(CancellationToken.None),
    schedule.Cron,
    new RecurringJobOptions { TimeZone = timeZone });

var monitor = app.Services.GetRequiredService<IOptions<MonitorOptions>>().Value;
if (monitor.RunOnStart)
    BackgroundJob.Enqueue<DailyScanJob>(job => job.ExecuteAsync(CancellationToken.None));

var calendarBaseUrl = monitor.CalendarUrlBase.TrimEnd('/');
app.MapGet("/prices", (PriceStore store) => Results.Content(
    CalendarPage.Index("Мониторинг туров", monitor.Hotels.Select(h => (h.Id, h.Name)).ToList(), calendarBaseUrl),
    "text/html; charset=utf-8"));
app.MapGet("/prices/{hotelId:int}", async (int hotelId, PriceStore store, CancellationToken ct) =>
{
    var hotel = monitor.Hotels.FirstOrDefault(h => h.Id == hotelId);
    if (hotel is null)
        return Results.NotFound();

    var entries = (await store.GetCalendarAsync(ct))
        .Where(e => e.HotelId == hotelId)
        .Select(e => (e.DateFrom, e.BestPrice, e.PreviousPrice, e.Link, e.MinPrice, e.MaxPrice))
        .ToList();
    var windowStart = DateTime.Today.AddDays(monitor.StartOffsetDays);
    var html = CalendarPage.Hotel(hotel.Name, hotelId, entries, windowStart, monitor.DateRangeDays, calendarBaseUrl);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

/// <summary>Разрешает доступ к Hangfire-дашборду с любого хоста (по умолчанию — только localhost).</summary>
internal sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

public partial class Program;
