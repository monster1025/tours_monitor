using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourMonitor.Camoufox;
using TourMonitor.LevelTravel;
using TourMonitor.Notifications;
using TourMonitor.Storage;

namespace TourMonitor.Jobs;

/// <summary>
/// Ежедневный скан: для каждой даты заезда — поиск туров по обоим отелям,
/// сохранение цен, сравнение со вчерашним днём и отправка отчёта в Telegram.
/// </summary>
public sealed class DailyScanJob
{
    private readonly LevelTravelClient _client;
    private readonly PriceStore _store;
    private readonly TelegramNotifier _notifier;
    private readonly CamoufoxBrowserSession _browser;
    private readonly MonitorOptions _options;
    private readonly ILogger<DailyScanJob> _logger;

    public DailyScanJob(
        LevelTravelClient client,
        PriceStore store,
        TelegramNotifier notifier,
        CamoufoxBrowserSession browser,
        IOptions<MonitorOptions> options,
        ILogger<DailyScanJob> logger)
    {
        _client = client;
        _store = store;
        _notifier = notifier;
        _browser = browser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Скан запущен: отелей={Hotels}, дат={Dates}", _options.Hotels.Count, _options.DateRangeDays);
            await _store.EnsureCreatedAsync(ct);

            var today = DateTime.Today;
            var start = today.AddDays(_options.StartOffsetDays);
            var dates = Enumerable.Range(0, _options.DateRangeDays)
                .Select(offset => start.AddDays(offset))
                .ToList();

            var todayKey = today.ToString("yyyy-MM-dd");
            var scannedDates = 0;

            await Parallel.ForEachAsync(
                dates,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelDates), CancellationToken = ct },
                async (date, token) =>
                {
                    try
                    {
                        await ScanDateAsync(date, todayKey, token);
                        Interlocked.Increment(ref scannedDates);
                        _logger.LogInformation("Дата {Date} — готово ({Scanned}/{Total})", date.ToString("yyyy-MM-dd"), Volatile.Read(ref scannedDates), dates.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка скана даты {Date}", date.ToString("yyyy-MM-dd"));
                    }
                });

            var messages = await BuildReportAsync(todayKey, ct);
            if (messages is null)
            {
                _logger.LogWarning("Отчёт пуст — данных по отелям нет.");
                return;
            }

            var sent = await _notifier.SendMessagesAsync(messages, ct);
            _logger.LogInformation("Отчёт отправлен в Telegram: {Sent}/{Total}", sent, messages.Count);
        }
        finally
        {
            await _browser.CloseAsync();
        }
    }

    private async Task ScanDateAsync(DateTime date, string todayKey, CancellationToken ct)
    {
        var dateKey = date.ToString("yyyy-MM-dd");
        var searchParams = new List<SearchParam>();
        foreach (var hotel in _options.Hotels)
        {
            searchParams.Add(new SearchParam
            {
                HotelIds = hotel.Id,
                FromCity = _options.DepartureCity,
                FromCountry = _options.DepartureCountry,
                ToCity = hotel.ToCity,
                ToCountry = hotel.ToCountry,
                StartDate = dateKey,
                Nights = hotel.Nights.ToString(),
                Adults = _options.Adults,
                Kids = _options.KidsAges.Count,
                KidsAges = _options.KidsAges.ToArray(),
                FlexDates = false,
                SearchType = "package",
            });
        }

        var enqueue = await _client.MultiEnqueueAsync(searchParams, ct);
        if (!enqueue.Success || enqueue.SearchRequests.Count != _options.Hotels.Count)
        {
            _logger.LogWarning("multi_enqueue для {Date} вернул {Count} поисков (ожидалось {Expected})",
                dateKey, enqueue.SearchRequests.Count, _options.Hotels.Count);
            return;
        }

        for (var i = 0; i < _options.Hotels.Count; i++)
        {
            var hotel = _options.Hotels[i];
            var requestId = enqueue.SearchRequests[i].RequestId;

            var status = await _client.WaitForSearchAsync(
                requestId, _options.PollIntervalSeconds, _options.SearchTimeoutSeconds, ct);

            var rates = await _client.GetRoomRatesAsync(hotel.Id.ToString(), requestId, ct);
            var offers = rates.Success
                ? FlattenOffers(hotel.Id, dateKey, rates)
                : new List<StoredOffer>();

            if (offers.Count == 0)
            {
                _logger.LogInformation("Отель {Hotel} на {Date}: туров нет (статус: {Status})",
                    hotel.Name, dateKey, string.Join(",", status.Status?.Values.Distinct() ?? Array.Empty<string>()));
            }

            await _store.SaveScanAsync(hotel.Id, dateKey, todayKey, offers, ct);
        }
    }

    private static List<StoredOffer> FlattenOffers(int hotelId, string dateKey, RoomRatesResponse rates)
    {
        var offers = new List<StoredOffer>();
        if (rates.Result is null)
            return offers;
        foreach (var roomRate in rates.Result)
        {
            foreach (var (meal, tourOffers) in roomRate.Offers)
            {
                foreach (var offer in tourOffers)
                {
                    offers.Add(new StoredOffer(
                        hotelId,
                        dateKey,
                        meal,
                        roomRate.Room.Id,
                        roomRate.Room.NameRu,
                        offer.OperatorId,
                        offer.OperatorName,
                        offer.Price,
                        offer.PricePerNight,
                        offer.Id,
                        offer.Link));
                }
            }
        }
        return offers;
    }

    private async Task<IReadOnlyList<string>?> BuildReportAsync(string todayKey, CancellationToken ct)
    {
        var windowStart = DateTime.Today.AddDays(_options.StartOffsetDays);
        var windowEnd = windowStart.AddDays(_options.DateRangeDays);

        var todayBests = (await _store.GetTodayBestsAsync(todayKey, ct))
            .Where(best => DateTime.TryParse(best.DateFrom, out var date) && date >= windowStart && date < windowEnd)
            .ToList();
        if (todayBests.Count == 0)
            return null;

        var header = BuildHeader();
        var lines = new List<ReportLine>();

        foreach (var best in todayBests)
        {
            var offer = await _store.GetBestOfferAsync(best.HotelId, best.DateFrom, todayKey, ct);
            var previous = await _store.GetBestBeforeAsync(best.HotelId, best.DateFrom, todayKey, ct);
            var historical = await _store.GetHistoricalMinBeforeAsync(best.HotelId, best.DateFrom, todayKey, ct);

            lines.Add(new ReportLine(
                best.HotelId,
                best.DateFrom,
                best.BestPrice,
                offer?.Meal ?? "",
                offer?.RoomName ?? "",
                offer?.OperatorName ?? "",
                offer?.Link ?? "",
                previous?.BestPrice,
                historical?.BestPrice));
        }

        return ReportBuilder.BuildMessages(
            header,
            _options.PricesPerMonth,
            _options.Hotels.Select(h => (h.Id, h.Name)).ToList(),
            lines,
            _options.CalendarUrlBase);
    }

    private string BuildHeader()
    {
        var nightsDesc = _options.Hotels.Count == 0
            ? ""
            : _options.Hotels.Select(h => h.Nights).Distinct().Count() == 1
                ? $"{_options.Hotels[0].Nights} ночей"
                : string.Join(", ", _options.Hotels.Select(h => $"{h.Name}: {h.Nights} ночей"));

        var paramParts = new List<string>
        {
            nightsDesc,
            $"{_options.Adults}+{_options.KidsAges.Count} чел",
        };
        if (_options.KidsAges.Count > 0)
            paramParts.Add($"ребёнок {string.Join(",", _options.KidsAges)} лет");

        var dates = Enumerable.Range(0, _options.DateRangeDays)
            .Select(offset => DateTime.Today.AddDays(_options.StartOffsetDays).AddDays(offset).ToString("dd.MM"))
            .ToList();
        var range = dates.Count == 0 ? "" : $"{dates[0]}–{dates[^1]}";

        return $"📊 <b>Мониторинг туров</b> · {string.Join(", ", paramParts)}\nЗаезды: {range}";
    }
}
