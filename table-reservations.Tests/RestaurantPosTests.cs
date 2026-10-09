using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Controllers;
using table_reservations.Models;
using table_reservations.Pos;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests;

public class RestaurantPosTests
{
    private static async Task MigrateAsync(ReservationRepositoryTests.Database db) =>
        await new table_reservations.Data.DatabaseInitializer(db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<table_reservations.Data.DatabaseInitializer>.Instance).MigrateAsync();

    [Fact]
    public async Task AcceptedCreateIsLocallyConfirmedAndRetriesDoNotCreateDuplicates()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos();
        var repo = Repository(db, pos);
        var start = ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18);
        var key = Guid.NewGuid().ToString();
        var request = new ReservationInfo { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1,2" };
        var result = await repo.BookWithIdempotencyAsync(request, start, key);
        Assert.Equal("Confirmed", result.Status);
        Assert.Equal(1, pos.Creates);
        Assert.Equal(0, pos.StatusReads);
        Assert.Equal(2, (await db.QueryAsync("SELECT * FROM table_reservations WHERE status = 'confirmed'")).Rows.Count);
        Assert.Empty(await repo.GetReminderCandidatesAsync());
        var tables = await repo.GetTablesAsync(start);
        Assert.Equal("occupied", tables[0].Status);
        Assert.Equal("occupied", tables[1].Status);
        Assert.Equal(0, pos.StatusReads);
        var retry = await repo.BookWithIdempotencyAsync(request, start, key);
        Assert.Equal(result.Id, retry.Id);
        Assert.Equal("Confirmed", retry.Status);
        Assert.Equal(1, pos.Creates);
        var competing = new ReservationInfo { CustomerName = "Other", CustomerPhone = "+77010000002", TablesId = "1" };
        Assert.Equal("TABLE_TAKEN", (await Assert.ThrowsAsync<BookingException>(() => repo.BookWithIdempotencyAsync(competing, start, Guid.NewGuid().ToString()))).Code);
    }

    [Fact]
    public async Task ConfirmedFrontOfficeResultIsRequiredForLocalConfirmation()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { Result = new(true, null, null, "Confirmed") };
        var repo = Repository(db, pos);
        var result = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString());
        Assert.Equal("Confirmed", result.Status);
        Assert.Single((await db.QueryAsync("SELECT * FROM table_reservations WHERE status = 'confirmed'")).Rows);
        Assert.Equal("Confirmed", (await repo.GetBookingStatusAsync(result.Id))!.Status);
    }

    [Fact]
    public async Task AvailabilityDoesNotReadReservationStatusOrChangeLocalBooking()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { Result = new(true, null, null, "Confirmed") };
        var repo = Repository(db, pos);
        var start = ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18);
        await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" }, start, Guid.NewGuid().ToString());
        Assert.Equal("occupied", (await repo.GetTablesAsync(start))[0].Status);
        Assert.Single((await db.QueryAsync("SELECT id FROM table_reservations WHERE status = 'confirmed'")).Rows);
        Assert.Equal(0, pos.StatusReads);
    }

    private sealed class Notifications : IReservationNotificationService
    {
        public int Sent { get; private set; }
        public Task<ReservationNotificationResult> SendReservationNotificationsAsync(ReservationInfo reservation, DateTime dateTime, string label, CancellationToken ct)
        {
            Sent++;
            return Task.FromResult(new ReservationNotificationResult(true, true, true));
        }
        public Task<bool> SendReminderAsync(string reservationId, ReservationInfo reservation, DateTime dateTime, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task UnknownCreateOutcomeNeedsExplicitCheckWithoutResubmission()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { FailCreate = true };
        var repo = Repository(db, pos);
        var result = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString());
        Assert.Equal("Processing", result.Status);
        var notifications = new Notifications();
        pos.Result = new(false, null, null, "Unknown");
        Assert.Equal("Processing", (await repo.CheckPosBookingAsync(result.Id))!.Status);
        Assert.Equal(0, notifications.Sent);
        pos.Result = new(false, null, null, "Processing");
        Assert.Equal("Confirmed", (await repo.CheckPosBookingAsync(result.Id))!.Status);
        await repo.NotifyPosBookingAsync(result.Id, notifications);
        await repo.NotifyPosBookingAsync(result.Id, notifications);
        Assert.Equal(1, pos.Creates);
        Assert.Equal(1, notifications.Sent);
        Assert.Equal("Confirmed", (await repo.GetBookingStatusAsync(result.Id))!.Status);
    }

    [Fact]
    public async Task ExplicitControllerCheckConfirmsInProgressAndNotifiesOnce()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { FailCreate = true };
        var repo = Repository(db, pos);
        var booking = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString());
        pos.FailCreate = false;
        pos.Result = new(false, null, null, "Processing");
        var notifications = new Notifications();
        var controller = new ReservationsController(repo, notifications, null!, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ReservationsController>.Instance);

        var response = await controller.CheckReservationStatus(Guid.Parse(booking.Id), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        Assert.Equal("Confirmed", (await repo.GetBookingStatusAsync(booking.Id))!.Status);
        Assert.Equal(1, notifications.Sent);
        Assert.Equal(1, pos.Creates);
    }

    [Fact]
    public async Task ExplicitCheckDoesNotDispatchAnOrphanedHeldRequest()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos();
        var repo = Repository(db, pos);
        var result = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString());
        await db.ExecuteAsync("UPDATE restaurant_booking_operations SET state = 'held'");
        await repo.CheckPosBookingAsync(result.Id);
        Assert.Equal(1, pos.Creates);
        Assert.Equal("Rejected", (await repo.GetBookingStatusAsync(result.Id))!.Status);
        Assert.Empty((await db.QueryAsync("SELECT id FROM table_reservations WHERE status = 'pending'")).Rows);
    }

    [Fact]
    public async Task ExistingReservationCannotBeReplaced()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { Result = new(true, null, null, "Confirmed") };
        var repo = Repository(db, pos);
        var start = ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18);
        var original = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" }, start, Guid.NewGuid().ToString());
        var error = await Assert.ThrowsAsync<BookingException>(() => repo.BookWithIdempotencyAsync(
            new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "2" }, start.AddDays(1), Guid.NewGuid().ToString()));
        Assert.Equal("EXISTING_RESERVATION", error.Code);
        Assert.Equal("Confirmed", (await repo.GetBookingStatusAsync(original.Id))!.Status);
        Assert.Equal(1, pos.Creates);
    }

    [Fact]
    public async Task ExpiredLeaseCannotConfirmUntilManualCheckClaimsTheOperation()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { Result = new(true, null, null, "Confirmed"),
            OnCreate = async () => { await db.ExecuteAsync("UPDATE restaurant_booking_operations SET lease_until = datetime('now','-1 second')"); } };
        var repo = Repository(db, pos);
        var result = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString());
        Assert.Equal("Processing", result.Status);
        Assert.Single((await db.QueryAsync("SELECT id FROM table_reservations WHERE status = 'pending'")).Rows);
        await repo.CheckPosBookingAsync(result.Id);
        Assert.Equal("Confirmed", (await repo.GetBookingStatusAsync(result.Id))!.Status);
        Assert.Equal(1, pos.Creates);
    }

    [Fact]
    public async Task AvailabilityUsesLocalBookingAndLiveOrderState()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos { Result = new(true, null, null, "Confirmed") };
        var repo = Repository(db, pos);
        var start = ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18);
        var result = await repo.BookWithIdempotencyAsync(new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "1" }, start, Guid.NewGuid().ToString());
        pos.Snapshot = pos.Snapshot with { OccupiedTableIds = new HashSet<string> { "two" } };
        var tables = await repo.GetTablesAsync(start.AddHours(2));
        Assert.Equal("occupied", tables[0].Status);
        Assert.Equal("free", tables[1].Status);
        var listing = Assert.Single(await repo.GetReservationsAsync(DateOnly.FromDateTime(start)));
        Assert.Equal("1", listing.TablesId);
        Assert.Equal(BookingRules.Wire(start.AddHours(3)), listing.EndsAt);
    }
    internal sealed class FakePos : IRestaurantPosClient
    {
        public PosAvailabilitySnapshot Snapshot { get; set; } = new(DateTimeOffset.UtcNow, new HashSet<string>());
        public bool Fail { get; set; }
        public bool FailCreate { get; set; }
        public int Creates { get; private set; }
        public int StatusReads { get; private set; }
        public Func<Task>? OnCreate { get; set; }
        public ReservationResult Result { get; set; } = new(false, null, null, "Processing");
        public Task<PosAvailabilitySnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
            Fail ? throw new BookingException("Unavailable", 503, "POS_UNAVAILABLE") : Task.FromResult(Snapshot);
        public async Task<ReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken ct = default)
        {
            Creates++;
            if (FailCreate) throw new BookingException("Ambiguous timeout", 503, "POS_UNAVAILABLE");
            if (OnCreate is not null) await OnCreate();
            return Result with { ExternalReservationId = request.ReservationId };
        }
        public Task<ReservationResult> GetAsync(string id, CancellationToken ct = default)
        {
            StatusReads++;
            return Task.FromResult(Result with { ExternalReservationId = id });
        }
    }

    internal static TursoReservationRepository Repository(ReservationRepositoryTests.Database db, FakePos pos)
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions { Id = "thetochka", Pos = new() { Enabled = true, TableMappings = new() { [1] = "one", [2] = "two" } } });
        return new(db, tenant, Options.Create(new TursoOptions()), pos);
    }

    [Fact]
    public async Task ByTableOrdersOnlyBlockCurrentAvailability()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var pos = new FakePos();
        var future = ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18);
        pos.Snapshot = pos.Snapshot with { OccupiedTableIds = new HashSet<string> { "one" } };
        var repo = Repository(db, pos);
        Assert.Equal("occupied", (await repo.GetTablesAsync())[0].Status);
        var tables = await repo.GetTablesAsync(future);
        Assert.Equal("free", tables[0].Status);
        Assert.Equal("free", tables[1].Status);
        Assert.False(await repo.IsReservationTakenAsync("2", future));
        Assert.NotNull(tables[0].AvailabilityValidUntil);
    }

    [Fact]
    public async Task PosAvailabilityAndBookingUseOnlyMappedTables()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        await db.ExecuteAsync("INSERT INTO tables(id,type,capacity) VALUES(3,'Обычный',4)");
        var pos = new FakePos();
        var repo = Repository(db, pos);

        Assert.Equal(new[] { 1, 2 }, (await repo.GetTablesAsync()).Select(table => table.Id));

        var error = await Assert.ThrowsAsync<BookingException>(() => repo.BookWithIdempotencyAsync(
            new() { CustomerName = "Guest", CustomerPhone = "+77010000001", TablesId = "3" },
            ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(18), Guid.NewGuid().ToString()));
        Assert.Equal("TABLE_TAKEN", error.Code);
        Assert.Equal(0, pos.Creates);
    }

    [Fact]
    public async Task UnavailablePosDoesNotReturnLocalFreeTables()
    {
        using var db = new ReservationRepositoryTests.Database();
        await MigrateAsync(db);
        var error = await Assert.ThrowsAsync<BookingException>(() => Repository(db, new() { Fail = true }).GetTablesAsync());
        Assert.Equal(503, error.Status);
    }

    [Theory]
    [InlineData("2026-10-09T23:00:00", "2026-10-10T04:00:00")]
    [InlineData("2026-10-10T01:00:00", "2026-10-10T04:00:00")]
    public void BusinessDayIncludesAfterMidnight(string now, string closing) =>
        Assert.Equal(DateTime.Parse(closing), BookingRules.BusinessDayClosing(DateTime.Parse(now), new BookingTimeOptions()));
}