using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TourMonitor.Storage;

public sealed record StoredOffer(
    int HotelId,
    string DateFrom,
    string Meal,
    int? RoomId,
    string? RoomName,
    int OperatorId,
    string OperatorName,
    int Price,
    int PricePerNight,
    string OfferId,
    string Link);

public sealed record DailyBest(int HotelId, string DateFrom, string Date, int BestPrice, string OfferId);

/// <summary>Минимальная известная цена по (отель, дата заезда) — строка календаря цен.</summary>
/// <summary>
/// Данные календаря по конкретной дате тура: цена из последнего сканирования (BestPrice),
/// цена предыдущего скана (PreviousPrice, null если его не было), ссылка на бронирование
/// лучшего оффера (Link) и диапазон минимальной цены по датам сканирования (MinPrice–MaxPrice).
/// </summary>
public sealed record CalendarEntry(int HotelId, string DateFrom, int BestPrice, int? PreviousPrice, string Link, int MinPrice, int MaxPrice);

/// <summary>SQLite-хранилище истории цен и ежедневных минимумов по (отель, дата заезда).</summary>
public sealed class PriceStore
{
    private readonly string _connectionString;
    private readonly ILogger<PriceStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PriceStore(string dbPath, ILogger<PriceStore>? logger = null)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Cache = SqliteCacheMode.Shared }.ToString();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS price_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    hotel_id INTEGER NOT NULL,
                    date_from TEXT NOT NULL,
                    checked_at TEXT NOT NULL,
                    meal TEXT NOT NULL DEFAULT '',
                    room_id INTEGER,
                    room_name TEXT,
                    operator_id INTEGER NOT NULL,
                    operator_name TEXT NOT NULL DEFAULT '',
                    price INTEGER NOT NULL,
                    price_per_night INTEGER,
                    offer_id TEXT NOT NULL DEFAULT '',
                    link TEXT NOT NULL DEFAULT '',
                    is_best INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_history ON price_history (hotel_id, date_from, checked_at);

                CREATE TABLE IF NOT EXISTS daily_best (
                    hotel_id INTEGER NOT NULL,
                    date_from TEXT NOT NULL,
                    date TEXT NOT NULL,
                    best_price INTEGER NOT NULL,
                    offer_id TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY (hotel_id, date_from, date)
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        });
    }

    /// <summary>Сохраняет офферы скана; лучший по цене помечается и пишется в daily_best.</summary>
    public async Task SaveScanAsync(int hotelId, string dateFrom, string date, IReadOnlyList<StoredOffer> offers, CancellationToken ct = default)
    {
        var checkedAt = DateTime.UtcNow.ToString("O");
        var best = offers.MinBy(o => o.Price);

        await ExecuteAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO price_history
                    (hotel_id, date_from, checked_at, meal, room_id, room_name, operator_id, operator_name,
                     price, price_per_night, offer_id, link, is_best)
                VALUES
                    ($hotel_id, $date_from, $checked_at, $meal, $room_id, $room_name, $operator_id, $operator_name,
                     $price, $price_per_night, $offer_id, $link, $is_best)
                """;
            insert.Parameters.Add("$hotel_id", SqliteType.Integer);
            insert.Parameters.Add("$date_from", SqliteType.Text);
            insert.Parameters.Add("$checked_at", SqliteType.Text);
            insert.Parameters.Add("$meal", SqliteType.Text);
            insert.Parameters.Add("$room_id", SqliteType.Integer);
            insert.Parameters.Add("$room_name", SqliteType.Text);
            insert.Parameters.Add("$operator_id", SqliteType.Integer);
            insert.Parameters.Add("$operator_name", SqliteType.Text);
            insert.Parameters.Add("$price", SqliteType.Integer);
            insert.Parameters.Add("$price_per_night", SqliteType.Integer);
            insert.Parameters.Add("$offer_id", SqliteType.Text);
            insert.Parameters.Add("$link", SqliteType.Text);
            insert.Parameters.Add("$is_best", SqliteType.Integer);

            foreach (var offer in offers)
            {
                insert.Parameters["$hotel_id"].Value = offer.HotelId;
                insert.Parameters["$date_from"].Value = offer.DateFrom;
                insert.Parameters["$checked_at"].Value = checkedAt;
                insert.Parameters["$meal"].Value = offer.Meal;
                insert.Parameters["$room_id"].Value = (object?)offer.RoomId ?? DBNull.Value;
                insert.Parameters["$room_name"].Value = (object?)offer.RoomName ?? DBNull.Value;
                insert.Parameters["$operator_id"].Value = offer.OperatorId;
                insert.Parameters["$operator_name"].Value = offer.OperatorName;
                insert.Parameters["$price"].Value = offer.Price;
                insert.Parameters["$price_per_night"].Value = (object?)offer.PricePerNight ?? DBNull.Value;
                insert.Parameters["$offer_id"].Value = offer.OfferId;
                insert.Parameters["$link"].Value = offer.Link;
                insert.Parameters["$is_best"].Value = offer == best ? 1 : 0;
                await insert.ExecuteNonQueryAsync(ct);
            }

            if (best is not null)
            {
                var upsert = connection.CreateCommand();
                upsert.CommandText = """
                    INSERT INTO daily_best (hotel_id, date_from, date, best_price, offer_id)
                    VALUES ($hotel_id, $date_from, $date, $price, $offer_id)
                    ON CONFLICT (hotel_id, date_from, date) DO UPDATE SET best_price = excluded.best_price, offer_id = excluded.offer_id
                    """;
                upsert.Parameters.AddWithValue("$hotel_id", hotelId);
                upsert.Parameters.AddWithValue("$date_from", dateFrom);
                upsert.Parameters.AddWithValue("$date", date);
                upsert.Parameters.AddWithValue("$price", best.Price);
                upsert.Parameters.AddWithValue("$offer_id", best.OfferId);
                await upsert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        });
    }

    /// <summary>Лучшая цена за дату проверки, предшествующую <paramref name="beforeDate"/> (ближайшая).</summary>
    public async Task<DailyBest?> GetBestBeforeAsync(int hotelId, string dateFrom, string beforeDate, CancellationToken ct = default)
    {
        DailyBest? result = null;
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT hotel_id, date_from, date, best_price, offer_id
                FROM daily_best
                WHERE hotel_id = $hotel_id AND date_from = $date_from AND date < $before
                ORDER BY date DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$hotel_id", hotelId);
            cmd.Parameters.AddWithValue("$date_from", dateFrom);
            cmd.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                result = ReadDailyBest(reader);
        });
        return result;
    }

    /// <summary>Исторический минимум до даты проверки (для определения «новой минималки»).</summary>
    public async Task<DailyBest?> GetHistoricalMinBeforeAsync(int hotelId, string dateFrom, string beforeDate, CancellationToken ct = default)
    {
        DailyBest? result = null;
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT hotel_id, date_from, date, best_price, offer_id
                FROM daily_best
                WHERE hotel_id = $hotel_id AND date_from = $date_from AND date < $before
                ORDER BY best_price ASC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$hotel_id", hotelId);
            cmd.Parameters.AddWithValue("$date_from", dateFrom);
            cmd.Parameters.AddWithValue("$before", beforeDate);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                result = ReadDailyBest(reader);
        });
        return result;
    }

    /// <summary>Лучшие предложения за сегодня по всем (отель, дата заезда).</summary>
    public async Task<List<DailyBest>> GetTodayBestsAsync(string date, CancellationToken ct = default)
    {
        var result = new List<DailyBest>();
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT hotel_id, date_from, date, best_price, offer_id FROM daily_best WHERE date = $date";
            cmd.Parameters.AddWithValue("$date", date);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(ReadDailyBest(reader));
        });
        return result;
    }

    /// <summary>
    /// Данные календаря по каждой дате тура: цена из последнего сканирования, цена предыдущего
    /// скана для дельты, ссылка на бронирование лучшего оффера и диапазон минимальной цены
    /// по всем датам сканирования (как «прыгала» цена самого дешёвого номера).
    /// </summary>
    public async Task<List<CalendarEntry>> GetCalendarAsync(CancellationToken ct = default)
    {
        var result = new List<CalendarEntry>();
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT db.hotel_id, db.date_from, db.best_price,
                       (SELECT p.best_price FROM daily_best p
                        WHERE p.hotel_id = db.hotel_id AND p.date_from = db.date_from
                          AND p.date < db.date
                        ORDER BY p.date DESC LIMIT 1) AS previous_price,
                       (SELECT ph.link FROM price_history ph
                        WHERE ph.hotel_id = db.hotel_id AND ph.date_from = db.date_from
                          AND ph.checked_at = (SELECT MAX(checked_at) FROM price_history
                                               WHERE hotel_id = db.hotel_id AND date_from = db.date_from)
                        ORDER BY ph.price ASC LIMIT 1) AS link,
                       (SELECT MIN(h.best_price) FROM daily_best h
                        WHERE h.hotel_id = db.hotel_id AND h.date_from = db.date_from) AS min_price,
                       (SELECT MAX(h2.best_price) FROM daily_best h2
                        WHERE h2.hotel_id = db.hotel_id AND h2.date_from = db.date_from) AS max_price
                FROM daily_best db
                JOIN (
                    SELECT hotel_id, date_from, MAX(date) AS latest
                    FROM daily_best
                    GROUP BY hotel_id, date_from
                ) m ON m.hotel_id = db.hotel_id AND m.date_from = db.date_from AND m.latest = db.date
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var previous = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var link = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var min = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                var max = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
                result.Add(new CalendarEntry(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), previous, link, min, max));
            }
        });
        return result;
    }

    private static DailyBest ReadDailyBest(SqliteDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4));

    /// <summary>Последний лучший оффер для (отель, дата заезда, дата проверки) — детали для отчёта.</summary>
    public async Task<StoredOffer?> GetBestOfferAsync(int hotelId, string dateFrom, string date, CancellationToken ct = default)
    {
        StoredOffer? result = null;
        await ExecuteAsync(async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT hotel_id, date_from, meal, room_id, room_name, operator_id, operator_name,
                       price, price_per_night, offer_id, link
                FROM price_history
                WHERE hotel_id = $hotel_id AND date_from = $date_from AND checked_at = (
                    SELECT MAX(checked_at) FROM price_history
                    WHERE hotel_id = $hotel_id AND date_from = $date_from
                )
                ORDER BY price ASC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$hotel_id", hotelId);
            cmd.Parameters.AddWithValue("$date_from", dateFrom);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                result = ReadOffer(reader);
        });
        return result;
    }

    private static StoredOffer ReadOffer(SqliteDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
            reader.GetString(9),
            reader.GetString(10));

    private async Task ExecuteAsync(Func<SqliteConnection, Task> action)
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await action(connection);
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class NullLogger : ILogger<PriceStore>, IDisposable
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => this;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        public void Dispose() { }
    }
}
