using table_reservations.Constants;
using table_reservations.Configuration;
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

        public IList<object> BuildReservationRow(ReservationInfo request, DateTime scheduledAt)
        {
            // Schema: id, plate number, reservation time, phone number, wash service type.
            return new List<object>
            {
                Guid.NewGuid().ToString(),
                request.PlateNumber ?? string.Empty,
                scheduledAt.ToString(ReservationDateTime.Format),
                request.CustomerPhone,
                request.WashServiceType ?? string.Empty
            };
        }

        public bool HasConflict(
            ReservationInfo request,
            DateTime scheduledAt,
            IList<object> existingRow,
            SheetSchemaOptions schema)
        {
            var existingPlate = GetCell(existingRow, schema.ResourceColumn);
            var existingTime = GetCell(existingRow, schema.ScheduledAtColumn);
            return string.Equals(existingPlate, request.PlateNumber?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   ReservationDateTime.TryParse(existingTime, out var existingStart) &&
                   existingStart == scheduledAt;
        }

        public string BuildNotificationLabel(ReservationInfo request, IReadOnlyList<TableInfo> tables) =>
            request.WashServiceType ?? string.Empty;

        public ReminderCandidate? MapReminderCandidate(
            IList<object> row,
            int sheetRowNumber,
            SheetSchemaOptions schema)
        {
            if (schema.RemindBeforeHourColumn < 0 || schema.ReminderSentColumn < 0)
            {
                return null;
            }

            var plateNumber = GetCell(row, schema.ResourceColumn);
            var scheduledAt = GetCell(row, schema.ScheduledAtColumn);
            if (string.IsNullOrWhiteSpace(plateNumber) && string.IsNullOrWhiteSpace(scheduledAt))
            {
                return null;
            }

            return new ReminderCandidate
            {
                SheetRowNumber = sheetRowNumber,
                Reservation = new ReservationInfo
                {
                    PlateNumber = plateNumber,
                    WashServiceType = GetCell(row, schema.ServiceTypeColumn),
                    CustomerPhone = GetCell(row, schema.CustomerPhoneColumn),
                    ScheduledAt = scheduledAt,
                    RemindBeforeHour = IsYes(GetCell(row, schema.RemindBeforeHourColumn))
                },
                RemindBeforeHourCell = GetCell(row, schema.RemindBeforeHourColumn),
                ReminderSentCell = GetCell(row, schema.ReminderSentColumn)
            };
        }

        private static string GetCell(IList<object> row, int index) =>
            index >= 0 && row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;

        private static bool IsYes(string value) =>
            string.Equals(value, "Да", StringComparison.OrdinalIgnoreCase);
    }
}
