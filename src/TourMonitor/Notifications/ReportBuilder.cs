using System.Globalization;
using System.Net;
using System.Text;

namespace TourMonitor.Notifications;

public sealed record ReportLine(
    int HotelId,
    string DateFrom,
    int Price,
    string Meal,
    string RoomName,
    string OperatorName,
    string Link,
    int? PreviousPrice,
    int? HistoricalMin);

/// <summary>Собирает HTML-сообщение для Telegram: секция на каждый отель с топом дешёвых дат и дельтами.</summary>
public static class ReportBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Собирает сообщение целиком (для обратной совместимости/тестов). Возвращает null, если нет данных ни по одному отелю.</summary>
    public static string? Build(
        string header,
        int pricesPerMonth,
        IReadOnlyList<(int HotelId, string Name)> hotels,
        IReadOnlyList<ReportLine> lines)
    {
        var messages = BuildMessages(header, pricesPerMonth, hotels, lines);
        return messages is null ? null : string.Join("\n\n", messages);
    }

    /// <summary>
    /// Собирает список сообщений для Telegram: первое — заголовок + первый отель,
    /// каждое следующее — отдельный отель. Сообщения длиннее лимита Telegram (4096)
    /// разбиваются на несколько. Возвращает null, если данных нет ни по одному отелю.
    /// </summary>
    public static IReadOnlyList<string>? BuildMessages(
        string header,
        int pricesPerMonth,
        IReadOnlyList<(int HotelId, string Name)> hotels,
        IReadOnlyList<ReportLine> lines,
        string? calendarUrlBase = null)
    {
        var messages = new List<string>();
        var first = true;
        foreach (var hotel in hotels)
        {
            var section = BuildHotelSection(
                hotel.Name,
                pricesPerMonth,
                lines.Where(l => l.HotelId == hotel.HotelId),
                calendarUrlBase is null ? null : $"{calendarUrlBase.TrimEnd('/')}/prices/{hotel.HotelId}");
            if (section is null)
                continue;
            messages.Add(first ? header + "\n\n" + section : section);
            first = false;
        }

        if (messages.Count == 0)
            return null;

        var result = new List<string>();
        foreach (var message in messages)
            result.AddRange(SplitMessage(message));
        return result;
    }

    /// <summary>
    /// Секция для одного отеля; null, если по отелю нет данных.
    /// В каждом месяце диапазона показывается до <paramref name="pricesPerMonth"/> дешёвых дат.
    /// </summary>
    public static string? BuildHotelSection(
        string hotelName,
        int pricesPerMonth,
        IEnumerable<ReportLine> lines,
        string? calendarUrl = null)
    {
        var byMonth = lines
            .Where(l => l.Price > 0)
            .GroupBy(l => MonthKey(l.DateFrom))
            .OrderBy(g => g.Key)
            .ToList();

        if (byMonth.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append("🏝 <b>").Append(HtmlEncode(hotelName)).Append("</b>");

        if (calendarUrl is not null)
        {
            sb.AppendLine();
            sb.Append("🗓 <a href=\"").Append(HtmlEncode(calendarUrl)).Append("\">календарь цен</a>");
        }

        foreach (var month in byMonth)
        {
            sb.AppendLine();
            sb.Append("📅 <b>").Append(HtmlEncode(MonthName(month.Key))).Append("</b>");

            foreach (var line in month.OrderBy(l => l.Price).ThenBy(l => l.DateFrom).Take(pricesPerMonth))
            {
                sb.AppendLine();
                sb.Append("   📅 ").Append(FormatDate(line.DateFrom)).Append(" — <b>")
                  .Append(FormatPrice(line.Price)).Append(" ₽</b>");

                var delta = DescribeDelta(line);
                if (delta is not null)
                    sb.Append(' ').Append(delta);

                sb.AppendLine();
                sb.Append("      🍽 ").Append(HtmlEncode(line.Meal)).Append(" · ");
                if (string.IsNullOrEmpty(line.Link))
                {
                    sb.Append(HtmlEncode(line.RoomName));
                }
                else
                {
                    sb.Append("<a href=\"").Append(HtmlEncode(FullLink(line.Link))).Append("\">")
                      .Append(HtmlEncode(line.RoomName)).Append("</a>");
                }
                sb.Append(" · ").Append(HtmlEncode(line.OperatorName));
            }
        }

        return sb.ToString();
    }

    /// <summary>Ключ месяца (yyyyMM) для группировки; неизвестные даты — в отдельной группе в конце.</summary>
    private static string MonthKey(string dateFrom)
    {
        if (DateTime.TryParse(dateFrom, out var date))
            return date.ToString("yyyyMM", Ru);
        return "999999";
    }

    private static string MonthName(string monthKey)
    {
        if (monthKey.Length == 6 && int.TryParse(monthKey, out var yyyyMM))
        {
            var name = new DateTime(yyyyMM / 100, yyyyMM % 100, 1).ToString("MMMM", Ru);
            return char.ToUpper(name[0]) + name[1..];
        }
        return "—";
    }

    /// <summary>
    /// Делит сообщение на части, не превышающие лимит Telegram (4096 симв.) — по границам
    /// строк, а слишком длинную одиночную строку режет принудительно.
    /// </summary>
    public static IEnumerable<string> SplitMessage(string text, int maxLength = 4000)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        if (text.Length <= maxLength)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var part in text.Split('\n'))
        {
            if (part.Length > maxLength)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                for (var i = 0; i < part.Length; i += maxLength)
                    yield return part.Substring(i, Math.Min(maxLength, part.Length - i));
                continue;
            }

            if (current.Length > 0 && current.Length + part.Length + 1 > maxLength)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0)
                current.Append('\n');
            current.Append(part);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string? DescribeDelta(ReportLine line)
    {
        if (line.PreviousPrice is null or <= 0)
            return null;

        var deltaPercent = (double)(line.Price - line.PreviousPrice.Value) / line.PreviousPrice.Value * 100;
        var label = deltaPercent switch
        {
            < 0 => $"🔻 −{Math.Abs(deltaPercent):0}% (было {FormatPrice(line.PreviousPrice.Value)} ₽)",
            > 0 => $"🔺 +{deltaPercent:0}% (было {FormatPrice(line.PreviousPrice.Value)} ₽)",
            _ => "= (без изменений)",
        };

        if (line.HistoricalMin is { } min && line.Price < min)
            return $"✨ новая минималка · {label}";

        return label;
    }

    private static string FullLink(string link) =>
        string.IsNullOrEmpty(link) ? "https://level.travel/" : "https://level.travel" + link;

    private static string FormatDate(string dateFrom) =>
        DateTime.TryParse(dateFrom, out var date) ? date.ToString("dd.MM", Ru) : dateFrom;

    private static string FormatPrice(int price) =>
        price.ToString("N0", Ru);

    private static string HtmlEncode(string value) =>
        WebUtility.HtmlEncode(value);
}
