# TourMonitor — мониторинг туров Level.Travel

Ежедневно проверяет цены на туры (9 ночей, 2 взрослых + ребёнок 5 лет) из Москвы в отели
**Sunrise Tucana Resort** и **Posh Club By Sunrise Tucana Resort** (Макади Бей, Египет)
на окно с +60 дней от сегодня на 90 дней вперёд и отправляет в Telegram отчёт с топом
дешёвых дат и дельтой цен.

Работает напрямую с публичным веб-API level.travel (подпись запросов и расшифровка
ответов воспроизведены с JS-бандла сайта), без партнёрского токена.

Запросы к API выполняются из настоящего браузера **Camoufox** (запатченный Firefox)
через Playwright: это обходит анти-бот периметр по фингерпринту, который режет даже
корректно подписанные запросы из обычного HTTP-клиента. Браузер скачивается и
устанавливается автоматически при первом запуске.

## Отчёт в Telegram

```
📊 Мониторинг туров · 9 ночей, 2+1 чел, ребёнок 5 лет
Заезды: 06.08–03.11

🏝 Sunrise Tucana Resort
📅 20.08 — 418 000 ₽ 🔻 −12% (было 475 447 ₽)
    🍽 AI · Полулюкс · Level.Travel
    🔗 забронировать
📅 02.09 — 501 000 ₽ ✨ новая минималка
...
```

Секция показывается только если по отелю есть предложения. Каждый отель приходит
**отдельным сообщением**; если сообщение длиннее лимита Telegram (4096 символов), оно
автоматически разбивается на несколько.

## Как запустить

Требуется .NET 10 SDK.

```bash
# первый скан сразу (наполнение базы) + отправка отчёта:
dotnet run --project src/TourMonitor -- Monitor__RunOnStart=true \
  --Telegram__BotToken=<токен> --Telegram__ChatId=<id>
```

- Дашборд Hangfire (журнал задач, кнопка «Run now»): http://localhost:5000/hangfire
- Календарь цен: http://localhost:5000/prices (список отелей → сетка дат с минимальной ценой)
- Ежедневный скан по умолчанию в 09:15 по Москве (`Schedule:Cron`, `Schedule:TimeZoneId`)
- База SQLite: `Data/tour_monitor.db` (история цен + ежедневные минимумы)

## Конфигурация

Всё через `appsettings.json` или переменные окружения (`LevelTravel__…`, `Monitor__…`,
`Telegram__…`, `Schedule__…`):

| Ключ | По умолчанию |
|---|---|
| `Telegram:BotToken`, `Telegram:ChatId` | пусто (отчёт не отправляется) |
| `Monitor:Hotels` | Sunrise Tucana 9099454, Posh Club 9151153, Riu Sri Lanka 9067553 |
| `Monitor:Adults`, `Monitor:KidsAges` | 2, `[5]` |
| `Monitor:DateRangeDays` | 150 |
| `Monitor:StartOffsetDays` | 30 (окно начинается с +30 дней от сегодня) |
| `Monitor:PricesPerMonth` | 3 (сколько дешёвых дат показывать в отчёте для каждого месяца) |
| `Monitor:CalendarUrlBase` | пусто (ссылка на календарь цен в отчёте; `http://192.168.1.6:8084` на хосте) |
| `Monitor:RunOnStart` | false |
| `Monitor:MaxParallelDates` | 5 (даты сканируются параллельно) |
| `Camoufox:Version` | `v152.0.4-beta.28` |
| `Camoufox:InstallDirectory` | `Data/camoufox` |
| `Camoufox:Headless` | true |
| `Schedule:Cron` | `0 15 9 * * ?` |
| `Schedule:TimeZoneId` | `Europe/Moscow` |

Ключи API и расшифровки (`LevelTravel:ApiKey`, `LevelTravel:SecretBoxKeys`) — из открытого
JS-бандла сайта; менять не нужно.

## Docker

```bash
cp .env.example .env   # заполнить TELEGRAM_BOT_TOKEN и TELEGRAM_CHAT_ID
docker compose up -d --build
```

Данные — в volume `tour-monitor-data` (`/app/Data/tour_monitor.db`).

## Тесты

```bash
dotnet test                       # юнит: подпись, расшифровка, хранилище, отчёт
LT_LIVE_TESTS=1 dotnet test       # + живые проверки против api.level.travel (через браузер Camoufox)
```

Живые тесты ходят в реальный API и поэтому опциональны: `LT_LIVE_TESTS=1` включает их,
без переменной они «проходят» без запросов. API защищён анти-бот периметром по
фингепринту/IP: в моменты блокировки он отвечает decoy'ами — `403`,
`{"message":"partner not found"}` (с кодом 200) или HTML-404 вместо данных.
Браузерный транспорт (Camoufox) эти блоки обходит и распознаёт decoy'ы.

## Структура

- `src/TourMonitor/LevelTravel/` — подпись (`SignHelper`), расшифровка (`LevelTravelCrypto`,
  XSalsa20-Poly1305 + zlib), клиент `multi_enqueue`/`status`/`room_rates` поверх
  транспорта `IApiTransport` (браузер Camoufox)
- `src/TourMonitor/Camoufox/` — установка Camoufox (скачивание из релизов), сессия
  браузера и fetch-транспорт через Playwright
- `src/TourMonitor/Storage/PriceStore.cs` — SQLite: история цен, ежедневные минимумы
- `src/TourMonitor/Jobs/DailyScanJob.cs` — сам скан: 90 дат × отели из конфига, параллельно
- `src/TourMonitor/Notifications/` — Telegram и сборка отчёта
- `tests/TourMonitor.Tests/` — юнит- и интеграционные тесты
