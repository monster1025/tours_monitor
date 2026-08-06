using TourMonitor.LevelTravel;

namespace TourMonitor.Tests;

public class SignTests
{
    /// <summary>
    /// Эталонный знак, снятый с реального запроса веб-клиента level.travel
    /// (GET /search/enqueue, зафиксирован в /tmp/lt_probe/cap_search/040-req.json).
    /// </summary>
    [Fact]
    public void GetEnqueueSign_Matches_CapturedRequest()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["start_date"] = "2026-08-20",
            ["nights"] = 9,
            ["adults"] = 2,
            ["kids"] = 0,
            ["kids_ages"] = "",
            ["from_city"] = "Moscow",
            ["from_country"] = "RU",
            ["to_city"] = "Hurghada",
            ["to_country"] = "EG",
            ["search_type"] = "package",
            ["flex_dates"] = 0,
        };

        var sign = SignHelper.ComputeGet("/search/enqueue", parameters, TestData.ApiKey, TestData.SignSalt, TestData.ApiVersion);

        Assert.Equal("60367bd68251236a1e0820c00a99cb92", sign);
    }

    /// <summary>
    /// Золотой знак для POST /search/multi_enqueue — снят с реального запроса сайта
    /// (captured: /tmp/lt_probe/cap_hotel, подпись в теле afaac79c2e0c81be5ce10fd584cc3d44).
    /// Проверяет полный путь: сериализация тела + флаттен + подпись.
    /// </summary>
    [Fact]
    public void PostMultiEnqueueSign_Matches_CapturedRequest()
    {
        var body = new MultiEnqueueRequest
        {
            SearchParams =
            {
                new SearchParam
                {
                    HotelIds = 9099454,
                    FromCity = "Moscow",
                    FromCountry = "RU",
                    ToCity = "Makadi Bay",
                    ToCountry = "EG",
                    StartDate = "2026-08-20",
                    Nights = "9",
                    Adults = 2,
                    Kids = 1,
                    FlexDates = false,
                    KidsAges = new[] { 5 },
                    SearchType = "package",
                },
                new SearchParam
                {
                    HotelIds = 9099454,
                    FromCity = "Moscow",
                    FromCountry = "RU",
                    ToCity = "Makadi Bay",
                    ToCountry = "EG",
                    StartDate = "2026-08-20",
                    Nights = "7..11",
                    Adults = 2,
                    Kids = 1,
                    FlexDates = true,
                    KidsAges = new[] { 5 },
                    SearchType = "package",
                },
            },
        };

        var sign = SignHelper.Compute(
            "/search/multi_enqueue",
            LevelTravelClient.GetSignValues(body),
            TestData.ApiKey,
            TestData.SignSalt);

        Assert.Equal("afaac79c2e0c81be5ce10fd584cc3d44", sign);
    }

    /// <summary>Знак не зависит от порядка параметров (JS-клиент сортирует значения).</summary>
    [Fact]
    public void Compute_IsOrderIndependent()
    {
        var values = new List<object?> { "2026-08-20", 9, 2, 0, "", "Moscow", "RU", "Hurghada", "EG", "package", 0 };
        var reversed = Enumerable.Reverse(values).ToList();

        var a = SignHelper.Compute("/search/enqueue", values, TestData.ApiKey, TestData.SignSalt);
        var b = SignHelper.Compute("/search/enqueue", reversed, TestData.ApiKey, TestData.SignSalt);

        Assert.Equal(a, b);
    }

    /// <summary>Строки обрезаются по краям, как в JS (value.trim()).</summary>
    [Fact]
    public void Flatten_TrimsStrings()
    {
        var signA = SignHelper.Compute("/search/status", new object?[] { " abc ", "1" }, TestData.ApiKey, TestData.SignSalt);
        var signB = SignHelper.Compute("/search/status", new object?[] { "abc", "1" }, TestData.ApiKey, TestData.SignSalt);

        Assert.Equal(signA, signB);
    }
}
