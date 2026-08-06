using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TourMonitor.Camoufox;
using TourMonitor.Jobs;
using TourMonitor.LevelTravel;
using TourMonitor.Notifications;
using TourMonitor.Storage;

namespace TourMonitor.Tests;

/// <summary>
/// Живые проверки публичного API Level.Travel через браузер Camoufox (единственный транспорт).
/// Включаются только явно:
///   LT_LIVE_TESTS=1 dotnet test
/// Без переменной окружения тесты «проходят» без выполнения запросов.
/// </summary>
public class LevelTravelIntegrationTests
{
    private const string SunriseTucana = "9099454";
    private const string PoshClub = "9151153";

    private static bool LiveEnabled => Environment.GetEnvironmentVariable("LT_LIVE_TESTS") == "1";

    private static LevelTravelOptions BuildOptions() =>
        new() { SecretBoxKeys = TestData.SecretBoxKeys.ToList() };

    private static async Task<CamoufoxBrowserSession> BuildSessionAsync()
    {
        var installDir = Path.Combine(Path.GetTempPath(), $"camoufox_test_{Guid.NewGuid():N}");
        var session = new CamoufoxBrowserSession(
            new CamoufoxInstaller(new CamoufoxReleaseResolver(), NullLogger<CamoufoxInstaller>.Instance),
            Options.Create(new CamoufoxOptions { InstallDirectory = installDir, Headless = true }),
            NullLogger<CamoufoxBrowserSession>.Instance);
        await session.GetPageAsync();
        return session;
    }

    private static LevelTravelClient BuildClient(CamoufoxBrowserSession session)
    {
        var transport = new CamoufoxApiTransport(session, Options.Create(BuildOptions()), NullLogger<CamoufoxApiTransport>.Instance);
        return new LevelTravelClient(transport, Options.Create(BuildOptions()), NullLogger<LevelTravelClient>.Instance);
    }

    private static List<SearchParam> BuildParams(string date)
    {
        var hotels = new[] { SunriseTucana, PoshClub };
        return hotels.Select(h => new SearchParam
        {
            HotelIds = int.Parse(h),
            FromCity = "Moscow",
            FromCountry = "RU",
            ToCity = "Makadi Bay",
            ToCountry = "EG",
            StartDate = date,
            Nights = "9",
            Adults = 2,
            Kids = 1,
            KidsAges = new[] { 5 },
            FlexDates = false,
            SearchType = "package",
        }).ToList();
    }

    [Fact]
    public async Task Live_Enqueue_Status_RoomRates_Flow()
    {
        if (!LiveEnabled)
            return;

        var session = await BuildSessionAsync();
        try
        {
            var client = BuildClient(session);
            var date = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd");

            var enqueue = await client.MultiEnqueueAsync(BuildParams(date));
            Assert.True(enqueue.Success);
            Assert.Equal(2, enqueue.SearchRequests.Count);

            var hotelIds = new[] { SunriseTucana, PoshClub };
            var totalOffers = 0;
            for (var i = 0; i < enqueue.SearchRequests.Count; i++)
            {
                var status = await client.WaitForSearchAsync(enqueue.SearchRequests[i].RequestId, 4, 240);
                Assert.True(status.Completeness >= 100 || status.Status?.Count > 0);

                var rates = await client.GetRoomRatesAsync(hotelIds[i], enqueue.SearchRequests[i].RequestId);
                if (rates.Success)
                    totalOffers += rates.Result.Sum(r => r.Offers.Values.Sum(o => o.Count));
            }

            Assert.True(totalOffers > 0, "API вернуло пустой результат по обоим отелям.");
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    [Fact]
    public async Task Live_DailyScan_Persists_Offers()
    {
        if (!LiveEnabled)
            return;

        var dbPath = Path.Combine(Path.GetTempPath(), $"tour_monitor_scan_{Guid.NewGuid():N}.db");
        var session = await BuildSessionAsync();
        try
        {
            var store = new PriceStore(dbPath);
            await store.EnsureCreatedAsync();

            var monitor = new MonitorOptions
            {
                Hotels =
                {
                    new() { Id = int.Parse(SunriseTucana), Name = "Sunrise Tucana Resort", ToCity = "Makadi Bay", ToCountry = "EG", Nights = 9 },
                    new() { Id = int.Parse(PoshClub), Name = "Posh Club By Sunrise Tucana Resort", ToCity = "Makadi Bay", ToCountry = "EG", Nights = 9 },
                },
                DateRangeDays = 1,
                KidsAges = { 5 },
                MaxParallelDates = 2,
                SearchTimeoutSeconds = 240,
            };

            var job = new DailyScanJob(
                BuildClient(session),
                store,
                new TelegramNotifier(new HttpClient(), Options.Create(new TelegramOptions()), NullLogger<TelegramNotifier>.Instance),
                session,
                Options.Create(monitor),
                NullLogger<DailyScanJob>.Instance);

            await job.ExecuteAsync();

            var bests = await store.GetTodayBestsAsync(DateTime.Today.ToString("yyyy-MM-dd"));
            Assert.NotEmpty(bests);
        }
        finally
        {
            await session.CloseAsync();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
