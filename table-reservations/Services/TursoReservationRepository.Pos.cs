using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using table_reservations.Constants;
using table_reservations.Data;
using table_reservations.Helpers;
using table_reservations.Models;

namespace table_reservations.Services;

public sealed partial class TursoReservationRepository
{
    private async Task<TursoRow?> OperationAsync(ITursoClient connection, string id, CancellationToken ct) =>
        (await connection.QueryAsync("SELECT * FROM restaurant_booking_operations WHERE id = ? AND organization_id = ?", [id, tenant.OrganizationId], ct)).Rows.SingleOrDefault();

    private static BookingResult Outcome(TursoRow operation) => new(operation.GetString("id"),
        DurationMinutes: ReservationDuration.Hours * 60,
        Status: operation.GetString("state") switch { "confirmed" or "locally_confirmed" => "Confirmed", "rejected" => "Rejected", _ => "Processing" }, PosManaged: true);

    public async Task<BookingResult> BookWithIdempotencyAsync(ReservationInfo request, DateTime scheduledAt, string? key, CancellationToken ct = default)
    {
        if (!UsesPos) return await BookAsync(request, scheduledAt, ct);
        EnsureTenant(false);
        if (!Guid.TryParse(key, out var parsedKey) || parsedKey == Guid.Empty)
            throw new BookingException("Требуется ключ запроса бронирования.", 400, "IDEMPOTENCY_KEY_REQUIRED");
        request.CustomerPhone = KazakhstanPhone.NormalizeAndValidate(request.CustomerPhone);
        request.CustomerName = request.CustomerName.Trim();
        request.ScheduledAt = BookingRules.Wire(scheduledAt);
        if (!BusinessTypes.RestaurantStrategy.TryParseTableIds(request.TablesId, out var tables) || tables.Length == 0 || tables.Distinct().Count() != tables.Length)
            throw new BookingException("Некорректные номера столиков.");
        request.TablesId = string.Join(",", tables.Order());
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var existing = (await db.QueryAsync("SELECT * FROM restaurant_booking_operations WHERE organization_id = ? AND idempotency_key = ?",
            [tenant.OrganizationId, parsedKey.ToString()], ct)).Rows.SingleOrDefault();
        if (existing is not null) return Replay(existing, hash);
        BookingRules.ValidateStart(scheduledAt, Hours, ReservationDateTime.KazakhstanNow());
        var snapshot = await Pos.GetSnapshotAsync(ct);
        var operationId = Guid.NewGuid().ToString();
        var held = await db.TransactionAsync(async (connection, token) => {
            var duplicate = (await connection.QueryAsync("SELECT * FROM restaurant_booking_operations WHERE organization_id = ? AND idempotency_key = ?",
                [tenant.OrganizationId, parsedKey.ToString()], token)).Rows.SingleOrDefault();
            if (duplicate is not null) return Replay(duplicate, hash);
            var state = await StateAsync(connection, false, token);
            var previous = state.Reservations.Where(row => row.Phone == request.CustomerPhone && row.End > ReservationDateTime.KazakhstanNow()).ToArray();
            if (previous.Length > 0)
                throw new BookingException("У вас уже есть актуальная бронь.", 409, "EXISTING_RESERVATION",
                    new ActiveReservationInfo { Id = previous[0].Id, CustomerName = previous[0].Name, CustomerPhone = request.CustomerPhone,
                        TablesId = previous[0].ResourceId, ScheduledAt = BookingRules.Wire(previous[0].Start), ScheduledAtValue = previous[0].Start });
            var end = scheduledAt.AddHours(ReservationDuration.Hours);
            var merged = WithPos(state, snapshot);
            if (tables.Any(table => !merged.Resources.Any(resource => resource.Id == table.ToString(CultureInfo.InvariantCulture))
                || !Free(merged, table.ToString(CultureInfo.InvariantCulture), scheduledAt, end)))
                throw new BookingException("Столик уже занят. Выберите другое время.", 409, "TABLE_TAKEN");
            await connection.ExecuteAsync("""
                INSERT INTO restaurant_booking_operations(id,organization_id,idempotency_key,request_hash,request_json,scheduled_at,state)
                VALUES(?,?,?,?,?,?,'held')
                """, [operationId, tenant.OrganizationId, parsedKey.ToString(), hash, payload, BookingRules.Store(scheduledAt),
                    ], token);
            foreach (var table in tables)
            {
                var rowId = Guid.NewGuid().ToString();
                await connection.ExecuteAsync("""
                    INSERT INTO table_reservations(id,table_id,customer_name,customer_phone,reserved_at,status,remind_before_hour)
                    VALUES(?,?,?,?,?,'pending',?)
                    """, [rowId, table, request.CustomerName, request.CustomerPhone, BookingRules.Store(scheduledAt), request.RemindBeforeHour], token);
                await connection.ExecuteAsync("INSERT INTO restaurant_booking_rows(operation_id,reservation_id) VALUES(?,?)", [operationId, rowId], token);
            }
            return new BookingResult(operationId, Status: "Processing", PosManaged: true);
        }, ct);
        if (held.Id != operationId) return held;
        await AdvanceOperationAsync(operationId, ct, allowDispatch: true);
        return Outcome((await OperationAsync(db, operationId, ct))!);
    }

    private static BookingResult Replay(TursoRow operation, string hash)
    {
        if (operation.GetString("request_hash") != hash) throw new BookingException("Ключ запроса уже использован для другой брони.", 409, "IDEMPOTENCY_CONFLICT");
        return Outcome(operation);
    }

    private async Task AdvanceOperationAsync(string id, CancellationToken ct, bool allowDispatch = false, bool acceptInProgress = false)
    {
        var lease = Guid.NewGuid().ToString();
        var claimed = await db.TransactionAsync(async (connection, token) => {
            await connection.ExecuteAsync("""
                UPDATE restaurant_booking_operations SET lease_token = ?, lease_until = datetime('now','+5 minutes'), attempts = attempts + 1
                WHERE id = ? AND organization_id = ? AND state IN ('held','dispatching','processing','review','local_accepting')
                  AND (lease_until IS NULL OR lease_until < CURRENT_TIMESTAMP)
                """, [lease, id, tenant.OrganizationId], token);
            var operation = await OperationAsync(connection, id, token);
            return operation?.GetString("lease_token") == lease ? operation : null;
        }, ct);
        if (claimed is null) return;
        var dispatched = claimed.GetString("state") != "held";
        var isInitialCreate = !dispatched;
        var locallyAccepted = claimed.GetString("state") == "local_accepting";
        try
        {
            if (!dispatched && !allowDispatch)
            {
                await FinishOperationAsync(id, lease, false, ct);
                return;
            }
            ReservationResult result;
            if (!dispatched)
            {
                var request = JsonSerializer.Deserialize<ReservationInfo>(claimed.GetString("request_json"), JsonOptions)!;
                var start = BookingRules.Read(claimed.GetString("scheduled_at"));
                BookingRules.ValidateStart(start, Hours, ReservationDateTime.KazakhstanNow());
                var snapshot = await Pos.GetSnapshotAsync(ct);
                var state = await StateAsync(db, false, ct);
                var ownIds = (await db.QueryAsync("SELECT reservation_id FROM restaurant_booking_rows WHERE operation_id = ?", [id], ct)).Rows.Select(row => row.GetString("reservation_id")).ToHashSet();
                var merged = WithPos(state with { Reservations = state.Reservations.Where(row => !ownIds.Contains(row.Id)).ToArray() }, snapshot);
                var tables = request.TablesId.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                if (tables.Any(table => !merged.Resources.Any(resource => resource.Id == table.ToString(CultureInfo.InvariantCulture))
                    || !Free(merged, table.ToString(CultureInfo.InvariantCulture), start, start.AddHours(ReservationDuration.Hours))))
                    throw new BookingException("Столик уже занят.", 409, "TABLE_TAKEN");
                await SetOperationStateAsync(id, lease, "dispatching", ct);
                dispatched = true;
                result = await Pos.CreateAsync(new(tenant.Organization!.Pos.OrganizationId!, new(request.CustomerName, request.CustomerPhone), start,
                    merged.Resources.Where(resource => tables.Contains(int.Parse(resource.Id, CultureInfo.InvariantCulture))).Sum(resource => resource.Capacity),
                    tables.Select(table => tenant.Organization.Pos.TableMappings[table]).ToList(), ReservationDuration.Hours * 60, id), ct);
                if (result.CorrelationId is not null)
                    await db.ExecuteAsync("UPDATE restaurant_booking_operations SET correlation_id = ? WHERE id = ? AND organization_id = ? AND lease_token = ?",
                        [result.CorrelationId, id, tenant.OrganizationId, lease], ct);
            }
            else result = await Pos.GetAsync(id, ct);

            locallyAccepted |= (isInitialCreate || acceptInProgress) && result.State == "Processing";
            if (locallyAccepted && claimed.GetString("state") != "local_accepting")
                await SetOperationStateAsync(id, lease, "local_accepting", ct);

            if (result.State == "Confirmed" || locallyAccepted)
            {
                if (!locallyAccepted && result.Reservation is { } confirmedReserve)
                {
                    var planned = JsonSerializer.Deserialize<ReservationInfo>(claimed.GetString("request_json"), JsonOptions)!;
                    var plannedIds = planned.TablesId.Split(',').Select(value => tenant.Organization!.Pos.TableMappings[int.Parse(value, CultureInfo.InvariantCulture)]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (confirmedReserve.EstimatedStartTime != BookingRules.Read(claimed.GetString("scheduled_at"))
                        || confirmedReserve.DurationInMinutes != ReservationDuration.Hours * 60 || !plannedIds.SetEquals(confirmedReserve.TableIds))
                    {
                        await SetOperationStateAsync(id, lease, "review", ct);
                        return;
                    }
                }
                await FinishOperationAsync(id, lease, true, ct, locallyAccepted ? "locally_confirmed" : null);
            }
            else if (result.State is "Rejected" or "Cancelled" or "Closed") await FinishOperationAsync(id, lease, false, ct);
            else await SetOperationStateAsync(id, lease, "processing", ct);
        }
        catch (BookingException) when (!dispatched)
        {
            await FinishOperationAsync(id, lease, false, CancellationToken.None);
            throw;
        }
        catch (Exception) when (dispatched)
        {
            await SetOperationStateAsync(id, lease, locallyAccepted ? "local_accepting" : "processing", CancellationToken.None);
        }
        finally
        {
            await db.ExecuteAsync("""
                UPDATE restaurant_booking_operations SET lease_until = NULL, lease_token = NULL
                WHERE id = ? AND organization_id = ? AND lease_token = ?
                """, [id, tenant.OrganizationId, lease], CancellationToken.None);
        }
    }

    private async Task SetOperationStateAsync(string id, string lease, string state, CancellationToken ct)
    {
        var updated = await db.QueryAsync("UPDATE restaurant_booking_operations SET state = ? WHERE id = ? AND organization_id = ? AND lease_token = ? AND lease_until > CURRENT_TIMESTAMP RETURNING id",
            [state, id, tenant.OrganizationId, lease], ct);
        if (updated.Rows.Count != 1) throw new BookingException("Запрос обрабатывается другим процессом.", 409, "BOOKING_PROCESSING");
    }

    private Task FinishOperationAsync(string id, string lease, bool confirmed, CancellationToken ct, string? operationState = null) => db.TransactionAsync(async (connection, token) => {
        var ownership = await connection.QueryAsync("SELECT id FROM restaurant_booking_operations WHERE id = ? AND organization_id = ? AND lease_token = ? AND lease_until > CURRENT_TIMESTAMP",
            [id, tenant.OrganizationId, lease], token);
        if (ownership.Rows.Count != 1) return false;
        var operation = await OperationAsync(connection, id, token);
        if (operation?.GetString("lease_token") != lease) return false;
        await connection.ExecuteAsync("UPDATE table_reservations SET status = ? WHERE id IN (SELECT reservation_id FROM restaurant_booking_rows WHERE operation_id = ?)",
            [confirmed ? "confirmed" : "cancelled", id], token);
        await connection.ExecuteAsync("UPDATE restaurant_booking_operations SET state = ? WHERE id = ? AND organization_id = ? AND lease_token = ?",
            [operationState ?? (confirmed ? "confirmed" : "rejected"), id, tenant.OrganizationId, lease], token);
        return true;
    }, ct);

    public async Task<BookingResult?> GetBookingStatusAsync(string id, CancellationToken ct = default)
    {
        if (!UsesPos) return null;
        EnsureTenant(false);
        var operation = await OperationAsync(db, id, ct);
        return operation is null ? null : Outcome(operation);
    }

    public async Task<BookingResult?> CheckPosBookingAsync(string id, CancellationToken ct = default)
    {
        if (!UsesPos) return null;
        EnsureTenant(false);
        var operation = await OperationAsync(db, id, ct);
        if (operation is null) return null;
        if (operation.GetString("state") is "held" or "dispatching" or "processing" or "review" or "local_accepting")
            await AdvanceOperationAsync(id, ct, acceptInProgress: true);
        return await OperationAsync(db, id, ct) is { } updated ? Outcome(updated) : null;
    }

    public async Task NotifyPosBookingAsync(string id, IReservationNotificationService notifications, CancellationToken ct = default)
    {
        if (!UsesPos) return;
        EnsureTenant(false);
        var claim = await db.QueryAsync("""
            UPDATE restaurant_booking_operations SET notification_claimed = 1
            WHERE id = ? AND organization_id = ? AND state IN ('confirmed','locally_confirmed') AND notification_claimed = 0
            RETURNING request_json, scheduled_at
            """, [id, tenant.OrganizationId], ct);
        var operation = claim.Rows.SingleOrDefault();
        if (operation is null) return;
        var request = JsonSerializer.Deserialize<ReservationInfo>(operation.GetString("request_json"), JsonOptions)!;
        await notifications.SendReservationNotificationsAsync(request, BookingRules.Read(operation.GetString("scheduled_at")), request.TablesId, ct);
    }
}