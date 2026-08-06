using TourMonitor.Web;

namespace TourMonitor.Tests;

public class CalendarPageTests
{
    [Fact]
    public void Hotel_RendersMonthsWithPricesAndEmptyCells()
    {
        var start = new DateTime(2026, 10, 5);
        var html = CalendarPage.Hotel(
            "Sunrise Tucana Resort",
            9099454,
            new[]
            {
                ("2026-10-05", 475_447, (int?)null, "/package_details/offer-1", 410_000, 550_000),
                ("2026-10-06", 460_000, (int?)null, "/package_details/offer-2", 460_000, 460_000),
            },
            start,
            windowDays: 3,
            baseUrl: "http://192.168.1.6:8084");

        Assert.Contains("Sunrise Tucana Resort", html);
        Assert.Contains("Октябрь", html);
        Assert.Contains("475\u00A0447", html);
        Assert.Contains("460\u00A0000", html);
        Assert.Contains("<div class=\"cell nodata\">", html);
        Assert.Contains("Цена из последнего сканирования", html);
        Assert.Contains("<span class=\"range\">410\u00A0000–550\u00A0000</span>", html);
    }

    [Fact]
    public void Hotel_ShowsDeltaArrows_WithPercent()
    {
        var html = CalendarPage.Hotel(
            "Sunrise Tucana Resort",
            9099454,
            new[]
            {
                ("2026-10-05", 475_447, (int?)540_000, "", 0, 0), // дешевле
                ("2026-10-06", 460_000, (int?)450_000, "", 0, 0), // дороже
                ("2026-10-07", 500_000, (int?)500_000, "", 0, 0), // без изменений
            },
            new DateTime(2026, 10, 5),
            windowDays: 3,
            baseUrl: "");

        Assert.Contains("<span class=\"chg down\">↓ 12%</span>", html);
        Assert.Contains("<span class=\"chg up\">↑ 2%</span>", html);
        Assert.Contains("<span class=\"chg same\">=</span>", html);
    }

    [Fact]
    public void Hotel_NoArrow_WhenNoPreviousScan()
    {
        var html = CalendarPage.Hotel(
            "Sunrise Tucana Resort",
            9099454,
            new[] { ("2026-10-05", 475_447, (int?)null, "", 0, 0) },
            new DateTime(2026, 10, 5),
            windowDays: 1,
            baseUrl: "");

        Assert.DoesNotContain("<span class=\"chg", html);
    }

    [Fact]
    public void Hotel_PriceLinksToBooking_WhenLinkPresent()
    {
        var html = CalendarPage.Hotel(
            "Sunrise Tucana Resort",
            9099454,
            new[]
            {
                ("2026-10-05", 475_447, (int?)null, "/package_details/offer-1?hotel_id=9099454", 0, 0),
                ("2026-10-06", 460_000, (int?)null, "", 0, 0),
            },
            new DateTime(2026, 10, 5),
            windowDays: 2,
            baseUrl: "");

        Assert.Contains("<a class=\"p book\" href=\"https://level.travel/package_details/offer-1?hotel_id=9099454\">475\u00A0447</a>", html);
        Assert.Contains("<span class=\"p\">460\u00A0000</span>", html);
    }

    [Fact]
    public void Hotel_ShowsCheapestRoomRange_InCellsAndHint()
    {
        var html = CalendarPage.Hotel(
            "Sunrise Tucana Resort",
            9099454,
            new[]
            {
                ("2026-10-05", 475_447, (int?)null, "/package_details/offer-1", 290_494, 574_875),
                ("2026-10-06", 460_000, (int?)null, "", 100_000, 130_000),
            },
            new DateTime(2026, 10, 5),
            windowDays: 2,
            baseUrl: "");

        Assert.Contains("<span class=\"range\">290\u00A0494–574\u00A0875</span>", html);
        Assert.Contains("<span class=\"range\">100\u00A0000–130\u00A0000</span>", html);
    }

    [Fact]
    public void Index_ListsHotelsWithLinks()
    {
        var html = CalendarPage.Index(
            "Мониторинг туров",
            new[] { (9099454, "Sunrise Tucana Resort"), (9151153, "Posh Club By Sunrise Tucana Resort") },
            "http://192.168.1.6:8084");

        Assert.Contains("Sunrise Tucana Resort", html);
        Assert.Contains("Posh Club By Sunrise Tucana Resort", html);
        Assert.Contains("/prices/9099454", html);
        Assert.Contains("/prices/9151153", html);
    }
}
