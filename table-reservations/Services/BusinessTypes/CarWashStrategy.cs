using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;

namespace table_reservations.Services.BusinessTypes
{
    /// <summary>
    /// Car-wash reservation rules and sheet mapping. Uses the
    /// (id, plate number, reservation time, phone number, wash service type) schema
    /// and its own validation: required plate and wash-service fields plus a minimum
    /// lead time. Car washes are treated as open all day, so no working-hours check.
    /// </summary>
    public sealed class CarWashStrategy : IBusinessTypeStrategy
    {
        public BusinessType Type => BusinessType.CarWash;

        public ReservationValidationResult ValidateCreate(ReservationInfo request)
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.PlateNumber) ||
                string.IsNullOrWhiteSpace(request.CustomerPhone) ||
                string.IsNullOrWhiteSpace(request.ScheduledAt) ||
                string.IsNullOrWhiteSpace(request.WashServiceType))
            {
                return ReservationValidationResult.Invalid(
                    "Некорректные данные записи. Укажите гос. номер, телефон, время и тип мойки.");
            }

            if (!ReservationDateTime.TryParse(request.ScheduledAt, out var scheduledAt))
            {
                return ReservationValidationResult.Invalid(
                    $"Некорректный формат dateTime. Ожидается {ReservationDateTime.Format}.");
            }

            var minAllowedTime = ReservationDateTime.KazakhstanNow().AddMinutes(5);
            if (scheduledAt < minAllowedTime)
            {
                return ReservationValidationResult.Invalid(
                    $"Минимальное время записи — {minAllowedTime:HH:mm}. Выберите время позже.");
            }

            return ReservationValidationResult.Valid(scheduledAt);
        }

        public ReservationRecord BuildRecord(ReservationInfo request, DateTime scheduledAt)
        {
            return new ReservationRecord
            {
                PlateNumber = request.PlateNumber ?? string.Empty,
                WashServiceType = request.WashServiceType ?? string.Empty,
                CustomerName = request.CustomerName ?? string.Empty,
                CustomerPhone = request.CustomerPhone,
                ScheduledAt = scheduledAt,
                RemindBeforeHour = request.RemindBeforeHour
            };
        }

        public bool HasConflict(ReservationInfo request, DateTime scheduledAt, ReservationRecord existing)
        {
            return string.Equals(existing.PlateNumber, request.PlateNumber?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   existing.ScheduledAt == scheduledAt;
        }

        public string BuildNotificationLabel(ReservationInfo request, IReadOnlyList<TableInfo> tables) =>
            request.WashServiceType ?? string.Empty;

        public ReminderCandidate? MapReminderCandidate(ReservationRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.PlateNumber) && record.ScheduledAt == default)
            {
                return null;
            }

            return new ReminderCandidate
            {
                Id = record.Id,
                Reservation = new ReservationInfo
                {
                    PlateNumber = record.PlateNumber,
                    WashServiceType = record.WashServiceType,
                    CustomerPhone = record.CustomerPhone,
                    ScheduledAt = record.ScheduledAt.ToString(ReservationDateTime.Format),
                    RemindBeforeHour = record.RemindBeforeHour
                },
                RemindBeforeHour = record.RemindBeforeHour,
                ReminderSent = record.ReminderSent
            };
        }
    }
}
