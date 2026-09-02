using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Services
{
    public sealed class ReservationReminderService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReservationReminderService> _logger;

        public ReservationReminderService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReservationReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationReminderService запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Ошибка при обработке напоминаний");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ProcessRemindersAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var sheets = scope.ServiceProvider.GetRequiredService<IGoogleSheetsService>();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppNotificationService>();

            // Нужен метод, который читает Брони!A2:H и возвращает строки + номер строки в Sheet
            var rows = await sheets.GetReminderCandidatesAsync(ct);

            // Раньше здесь было "DateTime.UtcNow.AddHours(5)" — жёстко зашитый оффсет,
            // который расходился с остальным приложением (GoogleSheetsService.KazakhstanNow()
            // использует TimeZoneInfo "Asia/Almaty"). Любое несовпадение источника "текущего
            // времени Казахстана" — это и есть почва для сдвигов на несколько часов.
            // Используем единственный канонический источник времени во всём проекте.
            var now = ReservationDateTime.KazakhstanNow();

            foreach (var item in rows)
            {
                // G = "Да" / "Нет"
                if (!IsYes(item.RemindBeforeHourCell))
                    continue;

                // H уже отправлено
                if (IsYes(item.ReminderSentCell))
                    continue;

                if (!ReservationDateTime.TryParse(item.ScheduledAt, out var dateTime))
                {
                    _logger.LogWarning("Некорректная дата в строке {Row}: {Value}", item.SheetRowNumber, item.ScheduledAt);
                    continue;
                }

                // было: now <= dateTime && RemindBeforeHour
                // нужно: сейчас внутри часа до брони
                var remindAt = dateTime.AddHours(-1);
                if (now < remindAt || now >= dateTime)
                    continue;

                var reservation = new ReservationInfo
                {
                    TablesId = item.TablesId,
                    CustomerName = item.CustomerName,
                    CustomerPhone = item.CustomerPhone,
                    ScheduledAt = item.ScheduledAt,
                    Section = item.Section ?? string.Empty,
                    RemindBeforeHour = true
                };

                var sent = await whatsApp.SendReminderBeforeHourAsync(reservation, dateTime, ct);
                if (!sent)
                {
                    _logger.LogWarning("Не удалось отправить напоминание, строка {Row}", item.SheetRowNumber);
                    continue;
                }

                await sheets.MarkReminderSentAsync(item.SheetRowNumber, ct); // пишем H = "Да"
                _logger.LogInformation("Напоминание отправлено, строка {Row}, бронь {At}", item.SheetRowNumber, dateTime);
            }
        }

        private static bool IsYes(string? value) =>
            string.Equals(value?.Trim(), "Да", StringComparison.OrdinalIgnoreCase);
    }

}
