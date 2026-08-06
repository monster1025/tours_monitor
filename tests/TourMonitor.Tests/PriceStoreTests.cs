using TourMonitor.Storage;

namespace TourMonitor.Tests;

public class PriceStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tour_monitor_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static StoredOffer Offer(int price, string room = "Полулюкс", string operatorName = "Level.Travel", string? link = null, string dateFrom = "2026-08-20") =>
        new(9099454, dateFrom, "AI", 12, room, 77, operatorName, price, price / 9, "offer-1", link ?? $"/package_details/offer-1?hotel_id=9099454");

    private PriceStore NewStore() => new(_dbPath);

    [Fact]
    public async Task SaveScan_WritesHistory_AndDailyBest()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();

        var offers = new List<StoredOffer> { Offer(500_000), Offer(475_447) };
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-06", offers);

        var bests = await store.GetTodayBestsAsync("2026-08-06");
        var best = Assert.Single(bests);
        Assert.Equal(475_447, best.BestPrice);
        Assert.Equal("offer-1", best.OfferId);

        var offer = await store.GetBestOfferAsync(9099454, "2026-08-20", "2026-08-06");
        Assert.NotNull(offer);
        Assert.Equal(475_447, offer!.Price);
        Assert.Equal("Полулюкс", offer.RoomName);
        Assert.Equal("AI", offer.Meal);
    }

    [Fact]
    public async Task SaveScan_EmptyOffers_DoesNotCreateDailyBest()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();

        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-06", new List<StoredOffer>());

        Assert.Empty(await store.GetTodayBestsAsync("2026-08-06"));
    }

    [Fact]
    public async Task SecondScan_UpsertsDailyBest_AndKeepsHistory()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-06", new List<StoredOffer> { Offer(500_000) });
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-07", new List<StoredOffer> { Offer(450_000, "Люкс") });

        var todayBests = await store.GetTodayBestsAsync("2026-08-07");
        var best = Assert.Single(todayBests);
        Assert.Equal(450_000, best.BestPrice);

        var previous = await store.GetBestBeforeAsync(9099454, "2026-08-20", "2026-08-07");
        Assert.NotNull(previous);
        Assert.Equal(500_000, previous!.BestPrice);
        Assert.Equal("2026-08-06", previous.Date);

        var historical = await store.GetHistoricalMinBeforeAsync(9099454, "2026-08-20", "2026-08-07");
        Assert.Equal(500_000, historical!.BestPrice);
    }

    [Fact]
    public async Task HistoricalMin_TakesLowestOfAllPreviousDays()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-04", new List<StoredOffer> { Offer(500_000) });
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-05", new List<StoredOffer> { Offer(470_000) });
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-06", new List<StoredOffer> { Offer(490_000) });

        var historical = await store.GetHistoricalMinBeforeAsync(9099454, "2026-08-20", "2026-08-07");

        Assert.Equal(470_000, historical!.BestPrice);
    }

    [Fact]
    public async Task GetCalendar_ReturnsLastScanPrice_AndPreviousForDelta()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-05", new List<StoredOffer> { Offer(500_000) });
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-06", new List<StoredOffer> { Offer(440_000) });
        await store.SaveScanAsync(9099454, "2026-10-06", "2026-08-06", new List<StoredOffer> { Offer(470_000) });

        var entries = await store.GetCalendarAsync();

        var first = entries.Single(e => e.DateFrom == "2026-10-05");
        Assert.Equal(440_000, first.BestPrice); // цена из последнего скана, а не накопленный минимум
        Assert.Equal(500_000, first.PreviousPrice);

        var second = entries.Single(e => e.DateFrom == "2026-10-06");
        Assert.Equal(470_000, second.BestPrice);
        Assert.Null(second.PreviousPrice); // первого скана не было — дельты нет
    }

    [Fact]
    public async Task GetCalendar_Range_IsMinMaxOfBestPrice_OverScanDates()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        // одна и та же дата тура, три разные даты сканирования
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-05", new List<StoredOffer> { Offer(100_000, dateFrom: "2026-10-05") });
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-06", new List<StoredOffer> { Offer(130_000, dateFrom: "2026-10-05") });
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-07", new List<StoredOffer> { Offer(120_000, dateFrom: "2026-10-05") });

        var entry = (await store.GetCalendarAsync()).Single(e => e.DateFrom == "2026-10-05");

        Assert.Equal(120_000, entry.BestPrice); // из последнего скана
        Assert.Equal(100_000, entry.MinPrice);  // мин по датам сканирования
        Assert.Equal(130_000, entry.MaxPrice);  // макс по датам сканирования
    }

    [Fact]
    public async Task GetCalendar_ReturnsBookingLink_ForBestOfferOfLastScan()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-05",
            new List<StoredOffer>
            {
                Offer(500_000, "Стандарт", "ANEX Tour", link: "/package_details/offer-old", dateFrom: "2026-10-05"),
                Offer(475_000, "Делюкс", "Pegas", link: "/package_details/offer-best", dateFrom: "2026-10-05"),
            });
        await store.SaveScanAsync(9099454, "2026-10-05", "2026-08-06",
            new List<StoredOffer>
            {
                Offer(440_000, "Люкс", "Level.Travel", link: "/package_details/offer-latest", dateFrom: "2026-10-05"),
                Offer(620_000, "Стандарт", "Pegas", link: "/package_details/offer-expensive", dateFrom: "2026-10-05"),
            });

        var entry = (await store.GetCalendarAsync()).Single(e => e.DateFrom == "2026-10-05");

        Assert.Equal(440_000, entry.BestPrice);
        Assert.Equal("/package_details/offer-latest", entry.Link); // ссылка из последнего скана, а не старого
    }

    [Fact]
    public async Task GetBestOffer_ReturnsCheapestOfLatestScan()
    {
        var store = NewStore();
        await store.EnsureCreatedAsync();
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-06", new List<StoredOffer> { Offer(500_000), Offer(460_000, "Стандарт", "ANEX Tour") });
        await store.SaveScanAsync(9099454, "2026-08-20", "2026-08-07", new List<StoredOffer> { Offer(510_000), Offer(475_000, "Делюкс", "Pegas") });

        var offer = await store.GetBestOfferAsync(9099454, "2026-08-20", "2026-08-07");

        Assert.Equal(475_000, offer!.Price);
        Assert.Equal("Делюкс", offer.RoomName);
        Assert.Equal("Pegas", offer.OperatorName);
    }
}
