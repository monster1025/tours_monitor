using TourMonitor.Notifications;

namespace TourMonitor.Tests;

public class ReportBuilderTests
{
    private static ReportLine Line(
        int hotelId = 9099454,
        string dateFrom = "2026-08-20",
        int price = 475_447,
        string meal = "AI",
        string roomName = "Полулюкс",
        string operatorName = "Level.Travel",
        string link = "/package_details/offer-1",
        int? previous = null,
        int? historicalMin = null) =>
        new(hotelId, dateFrom, price, meal, roomName, operatorName, link, previous, historicalMin);

    [Fact]
    public void BuildHotelSection_GroupsByMonth_ShowsTopPricesPerMonth()
    {
        var lines = new[]
        {
            Line(dateFrom: "2026-08-30", price: 520_000),
            Line(dateFrom: "2026-08-20", price: 475_447),
            Line(dateFrom: "2026-09-02", price: 501_000),
            Line(dateFrom: "2026-09-03", price: 490_000),
        };

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 2, lines);

        Assert.NotNull(section);
        Assert.Contains("Sunrise Tucana Resort", section);
        Assert.Contains("<b>Август</b>", section);
        Assert.Contains("<b>Сентябрь</b>", section);
        Assert.Contains("20.08", section);
        Assert.Contains("30.08", section);
        Assert.Contains("03.09", section);
        Assert.Contains("02.09", section);

        var months = section.Split("\n📅 <b>");
        Assert.Equal(3, months.Length); // отель + Август + Сентябрь
        Assert.Equal(2, months[1].Split("\n   📅 ").Length - 1); // Август — 2 дешёвых
        Assert.Equal(2, months[2].Split("\n   📅 ").Length - 1); // Сентябрь — 2 дешёвых
    }

    [Fact]
    public void BuildHotelSection_TakesNoMoreThanPricesPerMonth_WithinEachMonth()
    {
        var lines = new[]
        {
            Line(dateFrom: "2026-08-10", price: 500_000),
            Line(dateFrom: "2026-08-11", price: 510_000),
            Line(dateFrom: "2026-08-12", price: 520_000),
            Line(dateFrom: "2026-08-13", price: 530_000),
        };

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 2, lines);

        Assert.NotNull(section);
        Assert.Contains("<b>Август</b>", section);
        Assert.Contains("10.08", section);
        Assert.Contains("11.08", section);
        Assert.DoesNotContain("12.08", section);
        Assert.DoesNotContain("13.08", section);
    }

    [Fact]
    public void DescribeDelta_ShowsDrop_WithPercentage()
    {
        var line = Line(price: 475_447, previous: 540_000);

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.Contains("🔻 −12% (было 540\u00A0000 ₽)", section);
    }

    [Fact]
    public void DescribeDelta_ShowsNoChange_WhenPricesEqual()
    {
        var line = Line(price: 475_447, previous: 475_447);

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.Contains("= (без изменений)", section);
    }

    [Fact]
    public void DescribeDelta_MarksNewMinimum()
    {
        var line = Line(price: 410_000, previous: 420_000, historicalMin: 415_000);

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.Contains("✨ новая минималка", section);
    }

    [Fact]
    public void DescribeDelta_OmitsDelta_WhenNoPrevious()
    {
        var line = Line(previous: null);

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.DoesNotContain("было", section);
        Assert.DoesNotContain("🔻", section);
        Assert.DoesNotContain("🔺", section);
    }

    [Fact]
    public void BuildHotelSection_ReturnsNull_WhenNoData()
    {
        Assert.Null(ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, Array.Empty<ReportLine>()));
    }

    [Fact]
    public void Build_IncludesHeader_AndOnlyNonEmptySections()
    {
        var sections = ReportBuilder.Build(
            "📊 <b>Мониторинг туров</b> · 9 ночей, 2+1 чел",
            5,
            new[] { (9099454, "Sunrise Tucana Resort"), (9151153, "Posh Club By Sunrise Tucana Resort") },
            new[] { Line(hotelId: 9099454) });

        Assert.NotNull(sections);
        Assert.Contains("Мониторинг туров", sections);
        Assert.Contains("Sunrise Tucana Resort", sections);
        Assert.DoesNotContain("Posh Club", sections);
    }

    [Fact]
    public void Build_ReturnsNull_WhenNoLines()
    {
        var result = ReportBuilder.Build(
            "header", 5,
            new[] { (9099454, "Sunrise Tucana Resort") },
            Array.Empty<ReportLine>());

        Assert.Null(result);
    }

    [Fact]
    public void Html_SpecialCharacters_AreEscaped()
    {
        var line = Line(roomName: "Стандарт <b>x</b>", operatorName: "ANEX & Co");

        var section = ReportBuilder.BuildHotelSection("Sunrise <i>Tucana</i>", 5, new[] { line });

        Assert.Contains("Sunrise &lt;i&gt;Tucana&lt;/i&gt;", section);
        Assert.Contains("Стандарт &lt;b&gt;x&lt;/b&gt;", section);
        Assert.Contains("ANEX &amp; Co", section);
    }

    [Fact]
    public void RoomName_IsLinked_InsteadOfReserveLink()
    {
        var line = Line(link: "/package_details/offer-1?hotel_id=9099454", roomName: "Делюкс с видом на сад");

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.Contains("https://level.travel/package_details/offer-1?hotel_id=9099454", section);
        Assert.Contains("<a href=\"https://level.travel/package_details/offer-1?hotel_id=9099454\">Делюкс с видом на сад</a>", section);
        Assert.DoesNotContain("забронировать", section);
        Assert.DoesNotContain("🔗", section);
    }

    [Fact]
    public void RoomName_IsNotLinked_WhenLinkEmpty()
    {
        var line = Line(link: "", roomName: "Делюкс с видом на сад");

        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { line });

        Assert.Contains("🍽 AI · Делюкс с видом на сад · Level.Travel", section);
        Assert.DoesNotContain("<a href=", section);
    }

    [Fact]
    public void BuildMessages_SplitsHotelsIntoSeparateMessages()
    {
        var messages = ReportBuilder.BuildMessages(
            "📊 <b>Мониторинг туров</b>",
            10,
            new[] { (9099454, "Sunrise Tucana Resort"), (9151153, "Posh Club By Sunrise Tucana Resort") },
            new[]
            {
                Line(hotelId: 9099454, price: 400000),
                Line(hotelId: 9151153, price: 500000),
            });

        Assert.NotNull(messages);
        Assert.Equal(2, messages.Count);
        Assert.Contains("Мониторинг туров", messages[0]);
        Assert.Contains("Sunrise Tucana Resort", messages[0]);
        Assert.DoesNotContain("Posh Club", messages[0]);
        Assert.Contains("Posh Club By Sunrise Tucana Resort", messages[1]);
        Assert.DoesNotContain("Мониторинг туров", messages[1]);
    }

    [Fact]
    public void BuildMessages_ReturnsNull_WhenNoLines()
    {
        var messages = ReportBuilder.BuildMessages(
            "header", 10,
            new[] { (9099454, "Sunrise Tucana Resort") },
            Array.Empty<ReportLine>());

        Assert.Null(messages);
    }

    [Fact]
    public void BuildHotelSection_WithCalendarUrl_AddsCalendarLink_RightAfterHotelName()
    {
        var section = ReportBuilder.BuildHotelSection(
            "Sunrise Tucana Resort", 5, new[] { Line(price: 400000) },
            calendarUrl: "http://192.168.1.6:8084/prices/9099454");

        Assert.Contains("календарь цен", section);
        Assert.Contains("http://192.168.1.6:8084/prices/9099454", section);
        Assert.Contains("Sunrise Tucana Resort</b>\n🗓 <a href=", section);
        Assert.True(section.IndexOf("🗓") < section.IndexOf("📅 <b>"));
    }

    [Fact]
    public void BuildHotelSection_WithoutCalendarUrl_HasNoCalendarLink()
    {
        var section = ReportBuilder.BuildHotelSection("Sunrise Tucana Resort", 5, new[] { Line(price: 400000) });

        Assert.DoesNotContain("календарь цен", section);
    }

    [Fact]
    public void SplitMessage_ChunksOversizedTextAtLineBoundaries()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"📅 10.10 — 475 447 ₽\n    🍽 AI · Полулюкс · Level.Travel").ToList();
        var text = string.Join("\n", lines);

        var chunks = ReportBuilder.SplitMessage(text, maxLength: 500).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 500));
        Assert.Equal(text, string.Join("\n", chunks));
    }
}
