using table_reservations.Constants;
using table_reservations.Configuration;
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
        private static readonly TimeSpan OpensAt = new(12, 0, 0);
        private static readonly TimeSpan ClosesAt = new(4, 0, 0);

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

            // Бар работает каждый день с 12:00 до 04:00 следующего дня.
            var time = scheduledAt.TimeOfDay;
            var isWithinWorkingHours = time >= OpensAt || time < ClosesAt;
            if (!isWithinWorkingHours)
            {
                return ReservationValidationResult.Invalid(
                    "Бронь доступна только на время работы бара: с 12:00 до 04:00.");
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

        public IList<object> BuildReservationRow(ReservationInfo request, DateTime scheduledAt)
        {
            if (!TryParseTableIds(request.TablesId, out var ids))
            {
                throw new ArgumentException("Некорректные TablesId.");
            }

            var tablesIdCell = string.Join(",", ids);

            return new List<object>
            {
                Guid.NewGuid().ToString(),
                tablesIdCell,
                request.CustomerName,
                request.CustomerPhone,
                scheduledAt.ToString(ReservationDateTime.Format),
                "",
                request.RemindBeforeHour ? "Да" : "Нет"
            };
        }

        public bool HasConflict(
            ReservationInfo request,
            DateTime scheduledAt,
            IList<object> existingRow,
            SheetSchemaOptions schema)
        {
            if (!TryParseTableIds(request.TablesId, out var requestedIds) ||
                !TryParseTableIds(GetCell(existingRow, schema.TableIdsColumn), out var existingIds) ||
                !ReservationDateTime.TryParse(GetCell(existingRow, schema.ScheduledAtColumn), out var existingStart))
            {
                return false;
            }

            var existingEnd = existingStart.AddHours(ReservationDuration.Hours);
            var requestedEnd = scheduledAt.AddHours(ReservationDuration.Hours);
            return existingIds.Intersect(requestedIds).Any() &&
                   existingStart < requestedEnd && existingEnd > scheduledAt;
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

        public ReminderCandidate? MapReminderCandidate(
            IList<object> row,
            int sheetRowNumber,
            SheetSchemaOptions schema)
        {
            var tablesId = GetCell(row, schema.TableIdsColumn);
            var scheduledAt = GetCell(row, schema.ScheduledAtColumn);
            var remindCell = GetCell(row, schema.RemindBeforeHourColumn);
            if (string.IsNullOrWhiteSpace(tablesId) &&
                string.IsNullOrWhiteSpace(scheduledAt) &&
                string.IsNullOrWhiteSpace(remindCell))
            {
                return null;
            }

            return new ReminderCandidate
            {
                SheetRowNumber = sheetRowNumber,
                Reservation = new ReservationInfo
                {
                    TablesId = tablesId,
                    CustomerName = GetCell(row, schema.CustomerNameColumn),
                    CustomerPhone = GetCell(row, schema.CustomerPhoneColumn),
                    ScheduledAt = scheduledAt,
                    RemindBeforeHour = true
                },
                RemindBeforeHourCell = remindCell,
                ReminderSentCell = GetCell(row, schema.ReminderSentColumn)
            };
        }

        private static string GetCell(IList<object> row, int index) =>
            index >= 0 && row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;

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
