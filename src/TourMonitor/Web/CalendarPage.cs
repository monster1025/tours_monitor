using System.Globalization;
using System.Net;
using System.Text;

namespace TourMonitor.Web;

/// <summary>
/// Генерирует простые HTML-страницы «календаря цен»: сетка месяцев, в каждой ячейке —
/// цена из последнего сканирования на эту дату заезда (ссылка на бронирование лучшего
/// оффера) или пусто, если цены нет, и стрелка с изменением против предыдущего скана.
/// </summary>
public static class CalendarPage
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Index(string title, IReadOnlyList<(int HotelId, string Name)> hotels, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.Append(HtmlOpen(title));
        sb.Append("<h1>📊 ").Append(HtmlEncode(title)).Append("</h1>");
        sb.Append("<p>Выберите отель, чтобы посмотреть цены по датам заезда:</p><div class=\"cards\">");
        foreach (var hotel in hotels)
        {
            var url = $"{baseUrl}/prices/{hotel.HotelId}";
            sb.Append("<a class=\"card\" href=\"").Append(HtmlEncode(url)).Append("\">")
              .Append("<h2>🏝 ").Append(HtmlEncode(hotel.Name)).Append("</h2>")
              .Append("<p>открыть календарь →</p></a>");
        }
        sb.Append("</div>");
        sb.Append(HtmlClose());
        return sb.ToString();
    }

    public static string Hotel(
        string hotelName,
        int hotelId,
        IReadOnlyList<(string DateFrom, int Price, int? Previous, string Link, int Min, int Max)> entries,
        DateTime windowStart,
        int windowDays,
        string baseUrl)
    {
        var byDate = new Dictionary<DateOnly, (int Price, int? Prev, string Link, int Min, int Max)>();
        foreach (var (dateFrom, price, prev, link, min, max) in entries)
            if (DateOnly.TryParse(dateFrom, out var d))
                byDate[d] = (price, prev, link, min, max);

        var months = new SortedDictionary<string, List<DateOnly>>();
        for (var i = 0; i < windowDays; i++)
        {
            var day = DateOnly.FromDateTime(windowStart.AddDays(i));
            var key = day.ToString("yyyy-MM", Ru);
            if (!months.TryGetValue(key, out var list))
                months[key] = list = new List<DateOnly>();
            list.Add(day);
        }

        var sb = new StringBuilder();
        sb.Append(HtmlOpen(hotelName));
        sb.Append("<a class=\"back\" href=\"").Append(HtmlEncode($"{baseUrl}/prices")).Append("\">← все отели</a>");
        sb.Append("<h1>🏝 ").Append(HtmlEncode(hotelName)).Append("</h1>");
        sb.Append("<p class=\"hint\">Цена из последнего сканирования · ")
          .Append(windowStart.ToString("dd.MM.yyyy", Ru)).Append(" — ")
          .Append(windowStart.AddDays(windowDays - 1).ToString("dd.MM.yyyy", Ru))
          .Append(" · под ценой — диапазон минимальной цены по датам сканирования</p>");

        foreach (var (monthKey, days) in months)
        {
            var first = days[0];
            var monthTitle = first.ToString("MMMM yyyy", Ru);
            sb.Append("<h2>").Append(HtmlEncode(char.ToUpper(monthTitle[0]) + monthTitle[1..])).Append("</h2>");
            sb.Append("<div class=\"calendar\">");
            foreach (var weekday in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
                sb.Append("<div class=\"dow\">").Append(weekday).Append("</div>");

            var leadDays = ((int)first.DayOfWeek + 6) % 7; // Пн = 0
            for (var i = 0; i < leadDays; i++)
                sb.Append("<div class=\"cell empty\"></div>");

            foreach (var day in days)
            {
                var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var priceInfo = byDate.GetValueOrDefault(day);
                if (priceInfo.Price > 0)
                {
                    sb.Append("<div class=\"cell price").Append(weekend ? " weekend" : "").Append("\">")
                      .Append("<span class=\"d\">").Append(day.Day).Append("</span>");

                    if (string.IsNullOrEmpty(priceInfo.Link))
                    {
                        sb.Append("<span class=\"p\">").Append(priceInfo.Price.ToString("N0", Ru)).Append("</span>");
                    }
                    else
                    {
                        sb.Append("<a class=\"p book\" href=\"").Append(HtmlEncode(FullLink(priceInfo.Link))).Append("\">")
                          .Append(priceInfo.Price.ToString("N0", Ru)).Append("</a>");
                    }

                    sb.Append(RangeHtml(priceInfo.Min, priceInfo.Max))
                      .Append(DeltaHtml(priceInfo.Price, priceInfo.Prev))
                      .Append("</div>");
                }
                else
                {
                    sb.Append("<div class=\"cell nodata").Append(weekend ? " weekend" : "").Append("\">")
                      .Append("<span class=\"d\">").Append(day.Day).Append("</span>")
                      .Append("<span class=\"p\">—</span></div>");
                }
            }
            sb.Append("</div>");
        }

        sb.Append(HtmlClose());
        return sb.ToString();
    }

    /// <summary>Диапазон минимальной цены на дату тура по датам сканирования; пусто, если данных нет.</summary>
    private static string RangeHtml(int min, int max)
    {
        if (min <= 0 || max < min)
            return "";
        return $"<span class=\"range\">{min.ToString("N0", Ru)}–{max.ToString("N0", Ru)}</span>";
    }

    /// <summary>Стрелка изменения цены против предыдущего скана; пусто, если предыдущего скана не было.</summary>
    private static string DeltaHtml(int price, int? previous)
    {
        if (previous is null or <= 0)
            return "";
        var diff = price - previous.Value;
        if (diff == 0)
            return "<span class=\"chg same\">=</span>";
        var pct = Math.Round((double)diff / previous.Value * 100);
        var arrow = diff < 0 ? "↓" : "↑";
        return $"<span class=\"chg {(diff < 0 ? "down" : "up")}\">{arrow} {Math.Abs(pct)}%</span>";
    }

    private static string FullLink(string link) =>
        string.IsNullOrEmpty(link) ? "https://level.travel/" : "https://level.travel" + link;

    private static string HtmlOpen(string title) =>
        "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<title>" + HtmlEncode(title) + "</title><style>" +
        "body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#0f172a;color:#e2e8f0;margin:0;padding:24px;}" +
        "h1{font-size:22px;margin:0 0 8px;}h2{font-size:16px;color:#94a3b8;margin:28px 0 8px;text-transform:capitalize;}" +
        ".back{color:#38bdf8;text-decoration:none;font-size:13px;}.hint{color:#94a3b8;font-size:13px;margin:0 0 8px;}" +
        ".cards{display:flex;gap:16px;flex-wrap:wrap;}.card{display:block;background:#1e293b;border:1px solid #334155;border-radius:12px;padding:20px;color:#e2e8f0;text-decoration:none;width:280px;}" +
        ".card h2{margin:0 0 6px;color:#e2e8f0;}.card p{color:#94a3b8;margin:0;font-size:13px;}" +
        ".calendar{display:grid;grid-template-columns:repeat(7,1fr);gap:6px;max-width:840px;}" +
        ".dow{text-align:center;font-size:11px;color:#64748b;padding:4px 0;}" +
        ".cell{background:#1e293b;border:1px solid #334155;border-radius:8px;padding:6px;text-align:center;min-height:52px;display:flex;flex-direction:column;justify-content:space-between;}" +
        ".cell .d{font-size:12px;color:#94a3b8;}.cell .p{font-size:12px;font-weight:600;color:#4ade80;}" +
        ".cell .p.book{text-decoration:none;}" +
        ".cell .range{font-size:10px;color:#94a3b8;}" +
        ".cell .chg{font-size:10px;font-weight:500;line-height:1.2;}.cell .chg.down{color:#4ade80;}.cell .chg.up{color:#fb923c;}.cell .chg.same{color:#64748b;}" +
        ".cell.nodata .p{color:#475569;font-weight:400;}" +
        ".cell.weekend{border-color:#3b82f6;}" +
        ".cell.empty{background:transparent;border-color:transparent;}" +
        "</style></head><body>";
    private static string HtmlClose() => "</body></html>";

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);
}
