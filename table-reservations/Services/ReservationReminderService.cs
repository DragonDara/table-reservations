using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services
{
    public sealed class ReservationReminderService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrganizationRegistry _registry;
        private readonly ILogger<ReservationReminderService> _logger;

        public ReservationReminderService(
            IServiceScopeFactory scopeFactory,
            OrganizationRegistry registry,
            ILogger<ReservationReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _registry = registry;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationReminderService запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessAllOrganizationsAsync(stoppingToken);
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

        private async Task ProcessAllOrganizationsAsync(CancellationToken ct)
        {
            foreach (var organization in _registry.All)
            {
                try
                {
                    await ProcessRemindersAsync(organization, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Ошибка при обработке напоминаний для организации {Org}", organization.Id);
                }
            }
        }

        private async Task ProcessRemindersAsync(Configuration.OrganizationOptions organization, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();

            // Настраиваем tenant-контекст для этого прохода, чтобы GoogleSheetsService
            // работал с таблицей и схемой нужной организации.
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenant.Set(organization);

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
                if (!IsYes(item.RemindBeforeHourCell))
                    continue;

                if (IsYes(item.ReminderSentCell))
                    continue;

                if (!ReservationDateTime.TryParse(item.Reservation.ScheduledAt, out var dateTime))
                {
                    _logger.LogWarning(
                        "Некорректная дата в строке {Row}: {Value}",
                        item.SheetRowNumber,
                        item.Reservation.ScheduledAt);
                    continue;
                }

                // было: now <= dateTime && RemindBeforeHour
                // нужно: сейчас внутри часа до брони
                var remindAt = dateTime.AddHours(-1);
                if (now < remindAt || now >= dateTime)
                    continue;

                var sent = await whatsApp.SendReminderBeforeHourAsync(item.Reservation, dateTime, ct);
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
