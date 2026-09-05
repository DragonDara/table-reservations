using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services;

/// <summary>The existing database separates tenants by business table, not organization_id.</summary>
public sealed class TursoReservationRepository(
    ITursoClient db, TenantContext tenant, IOptions<TursoOptions> options) : IReservationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Active = "'pending','confirmed','in_progress'";
    private bool IsCarWash => tenant.BusinessType == BusinessType.CarWash;
    private BookingTimeOptions Hours => tenant.Organization!.BookingTime;
    private void EnsureTenant(bool? carwash = null)
    {
        var expected = IsCarWash ? options.Value.CarWashOrganizationId : options.Value.RestaurantOrganizationId;
        if (!string.Equals(tenant.OrganizationId, expected, StringComparison.OrdinalIgnoreCase) ||
            (carwash.HasValue && IsCarWash != carwash.Value))
            throw new BookingException("Хранилище этой организации не настроено.", 404);
    }

    public async Task<CarWashCatalog> GetCarWashCatalogAsync(CancellationToken ct = default)
    {
        EnsureTenant(true);
        return await CatalogAsync(db, ct);
    }
    private async Task<CarWashCatalog> CatalogAsync(ITursoClient connection, CancellationToken ct)
    {
        var results = await connection.QueryBatchAsync([
            new("SELECT id, name FROM vehicle_categories ORDER BY rowid"),
            new("""
                SELECT s.id, s.name, s.is_package, p.vehicle_category_id, p.price_minor, p.duration_minutes
                FROM carwash_services s JOIN carwash_service_prices p ON p.service_id = s.id
                WHERE s.is_active = 1 ORDER BY s.rowid
                """),
            new("SELECT package_service_id, included_service_id FROM carwash_package_items")
        ], ct);
        return new(
            results[0].Rows.Select(r => new VehicleCategory(r.GetString("id"), r.GetString("name"))).ToArray(),
            results[1].Rows.Select(r => new CarWashService(r.GetString("id"), r.GetString("name"), r.GetBoolean("is_package"),
                r.GetString("vehicle_category_id"), PriceKzt(r.GetInt64("price_minor")),
                r["duration_minutes"] is null ? null : r.GetInt32("duration_minutes"))).ToArray(),
            results[2].Rows.Select(r => new PackageItem(r.GetString("package_service_id"), r.GetString("included_service_id"))).ToArray());
    }
    private long PriceKzt(long price)
    {
        if (!options.Value.CatalogPricesAreKzt && price % 100 != 0)
            throw new BookingException("Цена должна быть указана в целых тенге.", 503);
        return options.Value.CatalogPricesAreKzt ? price : price / 100;
    }

    private sealed record Resource(string Id, string Name, string Type, int Capacity);
    private sealed record Existing(string Id, string ResourceId, string Name, string Phone, string Plate, DateTime Start, DateTime End, bool Remind);
    private sealed record State(IReadOnlyList<Resource> Resources, IReadOnlyList<Existing> Reservations);
    private async Task<State> StateAsync(ITursoClient connection, bool carwash, CancellationToken ct)
    {
        var results = await connection.QueryBatchAsync([
            new(carwash ? "SELECT id, name FROM carwash_boxes WHERE is_active = 1 ORDER BY id"
                : "SELECT id, type, capacity FROM tables WHERE status = 'available' ORDER BY id"),
            new(carwash
                ? $"SELECT id, box_id AS resource_id, customer_name, customer_phone, plate_number, scheduled_at AS start_at, ends_at, 0 AS remind_before_hour FROM box_reservations WHERE status IN ({Active})"
                : $"SELECT id, table_id AS resource_id, customer_name, customer_phone, '' AS plate_number, reserved_at AS start_at, datetime(reserved_at, '+3 hours') AS ends_at, remind_before_hour FROM table_reservations WHERE status IN ({Active})")
        ], ct);
        return new(
            results[0].Rows.Select(r => new Resource(r.GetString("id"), r.GetString("name"), r.GetString("type"), r.GetInt32("capacity"))).ToArray(),
            results[1].Rows.Select(r => new Existing(r.GetString("id"), r.GetString("resource_id"), r.GetString("customer_name"),
                NormalizePhone(r.GetString("customer_phone")), r.GetString("plate_number").Trim().ToUpperInvariant(),
                BookingRules.Read(r.GetString("start_at")), BookingRules.Read(r.GetString("ends_at")), r.GetBoolean("remind_before_hour"))).ToArray());
    }
    private static bool Free(State state, string resourceId, DateTime start, DateTime end, string? replacingPhone = null) =>
        !state.Reservations.Any(r => r.ResourceId == resourceId && r.Phone != replacingPhone && BookingRules.Overlaps(start, end, r.Start, r.End));

    public async Task<CarWashAvailability> GetCarWashAvailabilityAsync(DateOnly date, CarWashSelection selection, CancellationToken ct = default)
    {
        EnsureTenant(true);
        var quote = BookingRules.Quote(await CatalogAsync(db, ct), selection.VehicleCategoryId, selection.ServiceIds, options.Value.DefaultCarWashMinutes);
        var state = await StateAsync(db, true, ct);
        var slots = BookingRules.Slots(date, Hours, ReservationDateTime.KazakhstanNow())
            .Where(start => BookingRules.FitsHours(start, quote.DurationMinutes, Hours) &&
                state.Resources.Any(box => Free(state, box.Id, start, start.AddMinutes(quote.DurationMinutes))))
            .Select(BookingRules.Wire).ToArray();
        return new(quote, slots);
    }

    public async Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(DateOnly date, DateTime now, CancellationToken ct = default)
    {
        EnsureTenant(false);
        var state = await StateAsync(db, false, ct);
        return BookingRules.Slots(date, Hours, now)
            .Where(start => state.Resources.Any(table => Free(state, table.Id, start, start.AddHours(ReservationDuration.Hours)))).ToArray();
    }
    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default)
    {
        EnsureTenant(false);
        var start = scheduledAt ?? ReservationDateTime.KazakhstanNow();
        var end = start.AddHours(ReservationDuration.Hours);
        var state = await StateAsync(db, false, ct);
        return state.Resources.Select(resource =>
        {
            var free = Free(state, resource.Id, start, end);
            var next = state.Reservations.Where(r => r.ResourceId == resource.Id && r.Start >= end).OrderBy(r => r.Start).FirstOrDefault();
            return new TableInfo {
                Id = int.Parse(resource.Id, CultureInfo.InvariantCulture), Seats = resource.Capacity,
                Type = resource.Type.Equals("VIP", StringComparison.OrdinalIgnoreCase) ? TableType.VIP : TableType.Обычный,
                Status = !free ? TableStatuses.Occupied : next is null ? TableStatuses.Free : TableStatuses.Limited,
                NextReservationHours = free && next is not null ? (next.Start - start).TotalHours : null
            };
        }).ToArray();
    }
    public async Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default)
    {
        EnsureTenant(false);
        var state = await StateAsync(db, false, ct);
        return !state.Resources.Any(r => r.Id == tableId) || !Free(state, tableId, scheduledAt, scheduledAt.AddHours(ReservationDuration.Hours));
    }

    public async Task<BookingResult> BookAsync(ReservationInfo request, DateTime scheduledAt, CancellationToken ct = default)
    {
        EnsureTenant();
        BookingRules.ValidateStart(scheduledAt, Hours, ReservationDateTime.KazakhstanNow());
        var phone = NormalizePhone(request.CustomerPhone);
        if (phone.Length is < 11 or > 16) throw new BookingException("Укажите полный номер телефона.");
        request.CustomerPhone = phone;
        // BEGIN IMMEDIATE serializes the complete read/check/write sequence across app instances.
        return await db.TransactionAsync(async (connection, token) =>
        {
            var carwash = IsCarWash;
            var quote = carwash ? BookingRules.Quote(await CatalogAsync(connection, token),
                request.VehicleCategoryId ?? "", request.ServiceIds ?? [], options.Value.DefaultCarWashMinutes) : null;
            var duration = quote?.DurationMinutes ?? ReservationDuration.Hours * 60;
            var end = scheduledAt.AddMinutes(duration);
            if (carwash && !BookingRules.FitsHours(scheduledAt, duration, Hours))
                throw new BookingException("Для выбранных услуг недостаточно времени до закрытия.");
            var state = await StateAsync(connection, carwash, token);
            var previous = state.Reservations.Where(r => r.Phone == phone && r.End > ReservationDateTime.KazakhstanNow()).OrderBy(r => r.Start).ToArray();
            if (previous.Length > 0 && !request.Overwrite)
            {
                var first = previous[0];
                throw new BookingException("У вас уже есть актуальная запись. Заменить её?", 409, "EXISTING_RESERVATION",
                    new ActiveReservationInfo { Id = first.Id, CustomerName = first.Name, CustomerPhone = phone,
                        TablesId = carwash ? "" : first.ResourceId, ScheduledAt = BookingRules.Wire(first.Start), ScheduledAtValue = first.Start });
            }
            var replacingPhone = request.Overwrite ? phone : null;
            string[] resourceIds;
            if (carwash)
            {
                var plate = request.PlateNumber?.Trim().ToUpperInvariant() ?? "";
                if (plate.Length is 0 or > 20) throw new BookingException("Укажите корректный гос. номер.");
                if (state.Reservations.Any(r => r.Plate == plate && r.Phone != replacingPhone && BookingRules.Overlaps(scheduledAt, end, r.Start, r.End)))
                    throw new BookingException("Автомобиль уже записан на это время.", 409, "SLOT_TAKEN");
                var box = state.Resources.FirstOrDefault(r => Free(state, r.Id, scheduledAt, end, replacingPhone))
                    ?? throw new BookingException("На это время свободных боксов уже нет.", 409, "SLOT_TAKEN");
                resourceIds = [box.Id];
                request.PlateNumber = plate;
                request.WashServiceType = string.Join(", ", quote!.Services.Select(s => s.Name));
            }
            else
            {
                if (!BusinessTypes.RestaurantStrategy.TryParseTableIds(request.TablesId, out var ids) || ids.Length == 0 || ids.Distinct().Count() != ids.Length)
                    throw new BookingException("Некорректные номера столиков.");
                resourceIds = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
                if (resourceIds.Any(id => !state.Resources.Any(r => r.Id == id) || !Free(state, id, scheduledAt, end, replacingPhone)))
                    throw new BookingException("Столик уже занят. Выберите другое время.", 409, "TABLE_TAKEN");
            }
            var statements = new List<TursoStatement>();
            foreach (var old in previous)
                statements.Add(new($"UPDATE {(carwash ? "box_reservations" : "table_reservations")} SET status = 'cancelled' WHERE id = ?", [old.Id]));
            var reservationId = Guid.NewGuid().ToString();
            for (var i = 0; i < resourceIds.Length; i++)
            {
                var id = i == 0 ? reservationId : Guid.NewGuid().ToString();
                if (carwash)
                {
                    statements.Add(new("""
                        INSERT INTO box_reservations
                        (id,box_id,vehicle_category_id,plate_number,customer_phone,customer_name,scheduled_at,ends_at,status,total_minor,services_json)
                        VALUES (?,?,?,?,?,?,?,?,'confirmed',?,?)
                        """, [id, resourceIds[i], request.VehicleCategoryId, request.PlateNumber, phone, request.CustomerName.Trim(),
                            BookingRules.Store(scheduledAt), BookingRules.Store(end), checked(quote!.TotalKzt * 100),
                            JsonSerializer.Serialize(quote.Services.Select(s => s.Id).ToArray(), JsonOptions)]));
                }
                else statements.Add(new("""
                    INSERT INTO table_reservations (id,table_id,customer_name,customer_phone,reserved_at,status,remind_before_hour)
                    VALUES (?,?,?,?,?,'confirmed',?)
                    """, [id, int.Parse(resourceIds[i], CultureInfo.InvariantCulture), request.CustomerName.Trim(), phone,
                        BookingRules.Store(scheduledAt), request.RemindBeforeHour]));
            }
            await connection.QueryBatchAsync(statements, token);
            return new BookingResult(reservationId, previous.Length > 0, carwash ? resourceIds[0] : null, quote?.TotalKzt, quote?.DurationMinutes);
        }, ct);
    }

    public async Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default)
    {
        EnsureTenant();
        if (IsCarWash) return []; // This schema/tenant does not enable customer reminders for car washes.
        var now = ReservationDateTime.KazakhstanNow();
        var result = await db.QueryAsync("""
            SELECT r.id,r.table_id,r.customer_name,r.customer_phone,r.reserved_at
            FROM table_reservations r
            LEFT JOIN reservation_reminders m ON m.reservation_id = r.id
            WHERE r.remind_before_hour = 1 AND r.status IN ('pending','confirmed')
              AND m.reservation_id IS NULL AND r.reserved_at > ? AND r.reserved_at <= ?
            """, [BookingRules.Store(now), BookingRules.Store(now.AddHours(1))], ct);
        return result.Rows.Select(r => new ReminderCandidate {
            Id = r.GetString("id"), RemindBeforeHour = true,
            Reservation = new ReservationInfo { TablesId = r.GetString("table_id"), CustomerName = r.GetString("customer_name"),
                CustomerPhone = r.GetString("customer_phone"), ScheduledAt = BookingRules.Wire(BookingRules.Read(r.GetString("reserved_at"))), RemindBeforeHour = true }
        }).ToArray();
    }
    public async Task MarkReminderSentAsync(string reservationId, CancellationToken ct)
    {
        EnsureTenant(false);
        await db.ExecuteAsync("INSERT OR IGNORE INTO reservation_reminders (reservation_id,sent_at) VALUES (?,CURRENT_TIMESTAMP)", [reservationId], ct);
    }

    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 10) digits = "7" + digits;
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        return "+" + digits;
    }
}
