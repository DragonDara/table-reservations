using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;

namespace table_reservations.Services.BusinessTypes
{
    /// <summary>
    /// Restaurant / bar reservation rules and sheet mapping. Preserves the original
    /// single-tenant behavior: minimum lead time, working hours 12:00–04:00, unique
    /// table ids, and the (id, tableIds, name, phone, start, status, remind) row layout.
    /// </summary>
    public sealed class RestaurantStrategy : IBusinessTypeStrategy
    {
        public BusinessType Type => BusinessType.Restaurant;

        public ReservationValidationResult ValidateCreate(ReservationInfo request)
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.TablesId) ||
                string.IsNullOrWhiteSpace(request.CustomerName) ||
                string.IsNullOrWhiteSpace(request.CustomerPhone) ||
                string.IsNullOrWhiteSpace(request.ScheduledAt) ||
                string.IsNullOrWhiteSpace(request.Section))
            {
                return ReservationValidationResult.Invalid("Некорректные данные бронирования.");
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
                    $"Минимальное время брони — {minAllowedTime:HH:mm}. Выберите время позже.");
            }

            if (!TryParseTableIds(request.TablesId, out var tableIds) || tableIds.Length == 0)
            {
                return ReservationValidationResult.Invalid("Некорректный номер столика.");
            }

            if (tableIds.Length != tableIds.Distinct().Count())
            {
                return ReservationValidationResult.Invalid(
                    "В одной броне нельзя указывать один и тот же номер столика несколько раз.");
            }

            return ReservationValidationResult.Valid(scheduledAt);
        }

        public ReservationRecord BuildRecord(ReservationInfo request, DateTime scheduledAt)
        {
            if (!TryParseTableIds(request.TablesId, out var ids))
            {
                throw new ArgumentException("Некорректные TablesId.");
            }

            return new ReservationRecord
            {
                TableIds = string.Join(",", ids),
                CustomerName = request.CustomerName,
                CustomerPhone = request.CustomerPhone,
                ScheduledAt = scheduledAt,
                Status = string.Empty,
                RemindBeforeHour = request.RemindBeforeHour
            };
        }

        public bool HasConflict(ReservationInfo request, DateTime scheduledAt, ReservationRecord existing)
        {
            if (!TryParseTableIds(request.TablesId, out var requestedIds) ||
                !TryParseTableIds(existing.TableIds, out var existingIds))
            {
                return false;
            }

            var existingEnd = existing.ScheduledAt.AddHours(ReservationDuration.Hours);
            var requestedEnd = scheduledAt.AddHours(ReservationDuration.Hours);
            return existingIds.Intersect(requestedIds).Any() &&
                   existing.ScheduledAt < requestedEnd && existingEnd > scheduledAt;
        }

        public string BuildNotificationLabel(ReservationInfo request, IReadOnlyList<TableInfo> tables)
        {
            if (!TryParseTableIds(request.TablesId, out var tableIds))
            {
                return string.Empty;
            }

            return string.Join(", ", tables
                .Where(table => tableIds.Contains(table.Id))
                .Select(table => table.Type == TableType.VIP ? "VIP" : "Обычный")
                .Distinct());
        }

        public ReminderCandidate? MapReminderCandidate(ReservationRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.TableIds) && record.ScheduledAt == default)
            {
                return null;
            }

            return new ReminderCandidate
            {
                Id = record.Id,
                Reservation = new ReservationInfo
                {
                    TablesId = record.TableIds,
                    CustomerName = record.CustomerName,
                    CustomerPhone = record.CustomerPhone,
                    ScheduledAt = record.ScheduledAt.ToString(ReservationDateTime.Format),
                    RemindBeforeHour = record.RemindBeforeHour
                },
                RemindBeforeHour = record.RemindBeforeHour,
                ReminderSent = record.ReminderSent
            };
        }

        internal static bool TryParseTableIds(string value, out int[] ids)
        {
            ids = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var list = new List<int>(parts.Length);
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var id) || id <= 0)
                {
                    return false;
                }

                list.Add(id);
            }

            ids = list.ToArray();
            return ids.Length > 0;
        }
    }
}
