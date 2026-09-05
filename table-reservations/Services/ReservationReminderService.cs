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

            // Настраиваем tenant-контекст для этого прохода, чтобы репозиторий
            // работал с данными нужной организации.
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenant.Set(organization);

            var reservations = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppNotificationService>();

            var rows = await reservations.GetReminderCandidatesAsync(ct);

            // Единственный канонический источник времени во всём проекте (Asia/Almaty).
            var now = ReservationDateTime.KazakhstanNow();

            foreach (var item in rows)
            {
                if (!item.RemindBeforeHour || item.ReminderSent)
                    continue;

                if (!ReservationDateTime.TryParse(item.Reservation.ScheduledAt, out var dateTime))
                {
                    _logger.LogWarning(
                        "Некорректная дата в брони {Id}: {Value}",
                        item.Id,
                        item.Reservation.ScheduledAt);
                    continue;
                }

                // сейчас должно быть внутри часа до брони
                var remindAt = dateTime.AddHours(-1);
                if (now < remindAt || now >= dateTime)
                    continue;

                var sent = await whatsApp.SendReminderBeforeHourAsync(item.Reservation, dateTime, ct);
                if (!sent)
                {
                    _logger.LogWarning("Не удалось отправить напоминание, бронь {Id}", item.Id);
                    continue;
                }

                await reservations.MarkReminderSentAsync(item.Id, ct);
                _logger.LogInformation("Напоминание отправлено, бронь {Id}, время {At}", item.Id, dateTime);
            }
        }
    }

}
