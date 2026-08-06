namespace TourMonitor;

public sealed class LevelTravelOptions
{
    public string BaseUrl { get; set; } = "https://api.level.travel";
    public string ApiKey { get; set; } = "0fe9fb2ff35679322db5429b18a53aee";
    public string SignSalt { get; set; } = "2qqRS1f8TyuF";
    public string ApiVersion { get; set; } = "3.14";
    public string Language { get; set; } = "ru";
    public string Country { get; set; } = "ru";
    public string Currency { get; set; } = "RUB";
    public List<string> SecretBoxKeys { get; set; } = new();
}

public sealed class HotelOptions
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Направление поиска отеля.</summary>
    public string ToCity { get; set; } = "";
    /// <summary>Страна направления.</summary>
    public string ToCountry { get; set; } = "";
    /// <summary>Количество ночей для отеля.</summary>
    public int Nights { get; set; } = 9;
}

public sealed class MonitorOptions
{
    public List<HotelOptions> Hotels { get; set; } = new();
    public string DepartureCity { get; set; } = "Moscow";
    public string DepartureCountry { get; set; } = "RU";
    public int Adults { get; set; } = 2;
    public List<int> KidsAges { get; set; } = new();
    public int DateRangeDays { get; set; } = 90;
    /// <summary>Окно скана начинается не с сегодняшнего дня, а с отступа в днях (минимум).</summary>
    public int StartOffsetDays { get; set; } = 60;
    /// <summary>Сколько дешёвых дат показывать в отчёте для каждого месяца диапазона.</summary>
    public int PricesPerMonth { get; set; } = 3;
    public bool RunOnStart { get; set; }
    public int MaxParallelDates { get; set; } = 5;
    public int PollIntervalSeconds { get; set; } = 4;
    public int SearchTimeoutSeconds { get; set; } = 180;
    public string DbPath { get; set; } = "Data/tour_monitor.db";
    /// <summary>Публичный базовый URL (для ссылки на календарь цен в отчёте). Пусто — ссылка не добавляется.</summary>
    public string CalendarUrlBase { get; set; } = "";
}

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
}

public sealed class ScheduleOptions
{
    public string Cron { get; set; } = "0 30 7 * * ?";
    public string TimeZoneId { get; set; } = "Europe/Moscow";
}

/// <summary>Настройки анти-детект браузера Camoufox (патченный Firefox + Playwright).</summary>
public sealed class CamoufoxOptions
{
    /// <summary>Тег релиза daijro/camoufox.</summary>
    public string Version { get; set; } = "v152.0.4-beta.28";
    /// <summary>Каталог установки (может быть относительным к корню приложения).</summary>
    public string InstallDirectory { get; set; } = "Data/camoufox";
    public string? DownloadUrlOverride { get; set; }
    public bool Headless { get; set; } = true;
    /// <summary>Страница для установки браузерной сессии и кук.</summary>
    public string SessionUrl { get; set; } = "https://level.travel/";
}
