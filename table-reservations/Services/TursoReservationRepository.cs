using System.Globalization;
using table_reservations.Constants;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services
{
    /// <summary>
    /// Turso (libSQL) backed reservation store. Every statement is parameterized and
    /// scoped to the current tenant via <c>organization_id</c>.
    /// </summary>
    public sealed class TursoReservationRepository : IReservationRepository
    {
        // Sortable storage format so range predicates work directly in SQL.
        private const string StorageFormat = "yyyy-MM-dd HH:mm:ss";

        private const string SelectColumns =
            "id, organization_id, table_ids, customer_name, customer_phone, scheduled_at, status, " +
            "remind_before_hour, reminder_sent, plate_number, wash_service_type, created_at";

        private readonly ITursoClient _db;
        private readonly TenantContext _tenant;
        private readonly IBusinessTypeStrategyResolver _strategyResolver;

        public TursoReservationRepository(
            ITursoClient db,
            TenantContext tenant,
            IBusinessTypeStrategyResolver strategyResolver)
        {
            _db = db;
            _tenant = tenant;
            _strategyResolver = strategyResolver;
        }

        private string OrganizationId => _tenant.OrganizationId;

        private IBusinessTypeStrategy Strategy => _strategyResolver.Resolve(_tenant.BusinessType);

        public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(
            DateTime? scheduledAt = null,
            CancellationToken ct = default)
        {
            var slotStart = scheduledAt ?? ReservationDateTime.KazakhstanNow();
            var slotEnd = slotStart.AddHours(ReservationDuration.Hours);

            var tablesResult = await _db.QueryAsync(
                "SELECT table_number, table_type, seats FROM tables WHERE organization_id = ? ORDER BY table_number",
                new object?[] { OrganizationId },
                ct);

            var tables = new List<TableInfo>(tablesResult.Rows.Count);
            foreach (var row in tablesResult.Rows)
            {
                var id = row.GetInt32("table_number");
                if (id <= 0)
                {
                    continue;
                }

                tables.Add(new TableInfo
                {
                    Id = id,
                    Type = ParseTableType(row.GetString("table_type")),
                    Seats = row.GetInt32("seats"),
                    Status = TableStatuses.Free
                });
            }

            if (tables.Count == 0)
            {
                return tables;
            }

            // Only reservations that can still overlap or follow the requested slot.
            var reservations = await LoadAsync(
                "scheduled_at >= ?",
                new object?[] { Format(slotStart.AddHours(-ReservationDuration.Hours)) },
                ct);

            foreach (var table in tables)
            {
                DateTime? nextStart = null;
                var isOccupied = false;

                foreach (var reservation in reservations)
                {
                    if (!TryParseTableIds(reservation.TableIds, out var reservationTableIds) ||
                        !reservationTableIds.Contains(table.Id))
                    {
                        continue;
                    }

                    var reservationStart = reservation.ScheduledAt;
                    var reservationEnd = reservationStart.AddHours(ReservationDuration.Hours);

                    if (reservationStart < slotEnd && reservationEnd > slotStart)
                    {
                        isOccupied = true;
                        break;
                    }

                    if (reservationStart >= slotEnd && (nextStart is null || reservationStart < nextStart.Value))
                    {
                        nextStart = reservationStart;
                    }
                }

                if (isOccupied)
                {
                    table.Status = TableStatuses.Occupied;
                    table.NextReservationHours = null;
                }
                else if (nextStart is not null)
                {
                    table.NextReservationHours = Math.Round((nextStart.Value - slotStart).TotalHours, 2);
                    table.Status = TableStatuses.Limited;
                }
                else
                {
                    table.Status = TableStatuses.Free;
                    table.NextReservationHours = null;
                }
            }

            return tables;
        }

        public async Task<bool> IsReservationTakenAsync(
            string tableId,
            DateTime scheduledAt,
            long? excludeReservationId = null,
            CancellationToken ct = default)
        {
            if (!TryParseTableIds(tableId, out var requestedIds))
            {
                return false;
            }

            var slotEnd = scheduledAt.AddHours(ReservationDuration.Hours);
            var overlapping = await LoadOverlappingAsync(scheduledAt, slotEnd, excludeReservationId, ct);

            foreach (var reservation in overlapping)
            {
                if (TryParseTableIds(reservation.TableIds, out var existingIds) &&
                    existingIds.Intersect(requestedIds).Any())
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> HasConflictAsync(
            ReservationInfo reservation,
            DateTime scheduledAt,
            long? excludeReservationId = null,
            CancellationToken ct = default)
        {
            var strategy = Strategy;
            var slotEnd = scheduledAt.AddHours(ReservationDuration.Hours);
            var candidates = await LoadOverlappingAsync(scheduledAt, slotEnd, excludeReservationId, ct);

            return candidates.Any(existing => strategy.HasConflict(reservation, scheduledAt, existing));
        }

        public async Task<bool> IsPhoneAlreadyReservedAsync(string customerPhone, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return false;
            }

            var result = await _db.QueryAsync(
                "SELECT COUNT(1) AS total FROM reservations WHERE organization_id = ? AND customer_phone = ?",
                new object?[] { OrganizationId, customerPhone.Trim() },
                ct);

            return result.Rows.Count > 0 && result.Rows[0].GetInt64("total") > 0;
        }

        public async Task<bool> HasReservationForPhoneAsync(
            string customerPhone,
            DateTime scheduledAt,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return false;
            }

            var slotEnd = scheduledAt.AddHours(ReservationDuration.Hours);

            var result = await _db.QueryAsync(
                """
                SELECT COUNT(1) AS total
                FROM reservations
                WHERE organization_id = ?
                  AND customer_phone = ?
                  AND scheduled_at < ?
                  AND datetime(scheduled_at, ?) > ?
                """,
                new object?[]
                {
                    OrganizationId,
                    customerPhone.Trim(),
                    Format(slotEnd),
                    DurationModifier,
                    Format(scheduledAt)
                },
                ct);

            return result.Rows.Count > 0 && result.Rows[0].GetInt64("total") > 0;
        }

        public async Task<ActiveReservationInfo?> FindActiveReservationByPhoneAsync(
            string customerPhone,
            CancellationToken ct = default)
        {
            var all = await FindAllActiveReservationsByPhoneAsync(customerPhone, ct);
            return all.OrderBy(r => r.ScheduledAtValue).FirstOrDefault();
        }

        public async Task<IReadOnlyList<ActiveReservationInfo>> FindAllActiveReservationsByPhoneAsync(
            string customerPhone,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return Array.Empty<ActiveReservationInfo>();
            }

            var now = ReservationDateTime.KazakhstanNow();

            var records = await LoadAsync(
                "customer_phone = ? AND datetime(scheduled_at, ?) > ?",
                new object?[] { customerPhone.Trim(), DurationModifier, Format(now) },
                ct);

            return records
                .Select(record => new ActiveReservationInfo
                {
                    Id = record.Id,
                    TablesId = record.TableIds,
                    CustomerName = record.CustomerName,
                    CustomerPhone = record.CustomerPhone,
                    ScheduledAt = record.ScheduledAt.ToString(ReservationDateTime.Format),
                    ScheduledAtValue = record.ScheduledAt
                })
                .OrderBy(r => r.ScheduledAtValue)
                .ToList();
        }

        public async Task<long> AppendReservationAsync(
            ReservationInfo reservation,
            DateTime scheduledAt,
            CancellationToken ct = default)
        {
            var record = Strategy.BuildRecord(reservation, scheduledAt);

            var result = await _db.QueryAsync(
                """
                INSERT INTO reservations
                    (organization_id, table_ids, customer_name, customer_phone, scheduled_at, status,
                     remind_before_hour, reminder_sent, plate_number, wash_service_type)
                VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
                """,
                new object?[]
                {
                    OrganizationId,
                    record.TableIds,
                    record.CustomerName,
                    record.CustomerPhone,
                    Format(record.ScheduledAt),
                    record.Status,
                    record.RemindBeforeHour,
                    record.PlateNumber,
                    record.WashServiceType
                },
                ct);

            return result.LastInsertRowId;
        }

        public async Task OverwriteReservationAsync(
            long reservationId,
            ReservationInfo reservation,
            DateTime scheduledAt,
            CancellationToken ct = default)
        {
            var record = Strategy.BuildRecord(reservation, scheduledAt);

            await _db.ExecuteAsync(
                """
                UPDATE reservations
                SET table_ids = ?,
                    customer_name = ?,
                    customer_phone = ?,
                    scheduled_at = ?,
                    status = ?,
                    remind_before_hour = ?,
                    reminder_sent = 0,
                    plate_number = ?,
                    wash_service_type = ?
                WHERE id = ? AND organization_id = ?
                """,
                new object?[]
                {
                    record.TableIds,
                    record.CustomerName,
                    record.CustomerPhone,
                    Format(record.ScheduledAt),
                    record.Status,
                    record.RemindBeforeHour,
                    record.PlateNumber,
                    record.WashServiceType,
                    reservationId,
                    OrganizationId
                },
                ct);
        }

        public Task DeleteReservationAsync(long reservationId, CancellationToken ct = default) =>
            _db.ExecuteAsync(
                "DELETE FROM reservations WHERE id = ? AND organization_id = ?",
                new object?[] { reservationId, OrganizationId },
                ct);

        public Task MarkReminderSentAsync(long reservationId, CancellationToken ct) =>
            _db.ExecuteAsync(
                "UPDATE reservations SET reminder_sent = 1 WHERE id = ? AND organization_id = ?",
                new object?[] { reservationId, OrganizationId },
                ct);

        public async Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default)
        {
            var strategy = Strategy;
            var records = await LoadAsync("remind_before_hour = 1 AND reminder_sent = 0", null, ct);

            var result = new List<ReminderCandidate>(records.Count);
            foreach (var record in records)
            {
                var candidate = strategy.MapReminderCandidate(record);
                if (candidate is not null)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        public bool TryParseTableIds(string value, out int[] ids) =>
            RestaurantStrategy.TryParseTableIds(value, out ids);

        private static string DurationModifier => $"+{ReservationDuration.Hours} hours";

        private async Task<IReadOnlyList<ReservationRecord>> LoadOverlappingAsync(
            DateTime slotStart,
            DateTime slotEnd,
            long? excludeReservationId,
            CancellationToken ct)
        {
            var predicate = "scheduled_at < ? AND datetime(scheduled_at, ?) > ?";
            var args = new List<object?> { Format(slotEnd), DurationModifier, Format(slotStart) };

            if (excludeReservationId.HasValue)
            {
                predicate += " AND id <> ?";
                args.Add(excludeReservationId.Value);
            }

            return await LoadAsync(predicate, args, ct);
        }

        private async Task<IReadOnlyList<ReservationRecord>> LoadAsync(
            string? predicate,
            IReadOnlyList<object?>? predicateArgs,
            CancellationToken ct)
        {
            var args = new List<object?> { OrganizationId };
            if (predicateArgs is not null)
            {
                args.AddRange(predicateArgs);
            }

            var sql = $"SELECT {SelectColumns} FROM reservations WHERE organization_id = ?";
            if (!string.IsNullOrWhiteSpace(predicate))
            {
                sql += $" AND ({predicate})";
            }

            sql += " ORDER BY scheduled_at";

            var result = await _db.QueryAsync(sql, args, ct);

            var records = new List<ReservationRecord>(result.Rows.Count);
            foreach (var row in result.Rows)
            {
                records.Add(new ReservationRecord
                {
                    Id = row.GetInt64("id"),
                    OrganizationId = row.GetString("organization_id"),
                    TableIds = row.GetString("table_ids"),
                    CustomerName = row.GetString("customer_name"),
                    CustomerPhone = row.GetString("customer_phone"),
                    ScheduledAt = ParseStorageDate(row.GetString("scheduled_at")),
                    Status = row.GetString("status"),
                    RemindBeforeHour = row.GetBoolean("remind_before_hour"),
                    ReminderSent = row.GetBoolean("reminder_sent"),
                    PlateNumber = row.GetString("plate_number"),
                    WashServiceType = row.GetString("wash_service_type"),
                    CreatedAt = ParseStorageDate(row.GetString("created_at"))
                });
            }

            return records;
        }

        internal static string Format(DateTime value) =>
            value.ToString(StorageFormat, CultureInfo.InvariantCulture);

        internal static DateTime ParseStorageDate(string value)
        {
            if (DateTime.TryParseExact(
                    value,
                    new[] { StorageFormat, "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed;
            }

            return ReservationDateTime.TryParse(value, out var legacy) ? legacy : default;
        }

        private static TableType ParseTableType(string value) =>
            string.Equals(value?.Trim(), "VIP", StringComparison.OrdinalIgnoreCase)
                ? TableType.VIP
                : TableType.Обычный;
    }
}
