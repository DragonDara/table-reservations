using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Controllers;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;
using Database = table_reservations.Tests.ReservationRepositoryTests.Database;

namespace table_reservations.Tests;

public class SecretVeilBookingRulesTests
{
    private static OrganizationOptions Organization()
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tenant-settings.json")).Build();
        return config.GetSection("Organizations").Get<OrganizationsOptions>()!.Items.Single(org => org.Id == "secret-veil");
    }
    private static async Task<Database> DatabaseWithInventory()
    {
        var db = new Database();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "secret-veil.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "secret-veil-tables.sql")));
        return db;
    }
    private static TenantContext Tenant(OrganizationOptions org) { var tenant = new TenantContext(); tenant.Set(org); return tenant; }
    private static TursoReservationRepository Repository(Database db, OrganizationOptions org) => new(db, Tenant(org), Options.Create(new TursoOptions()));
    private static DateTime Start => ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(14);
    private static ReservationInfo Request(int table, DateTime start, string phone = "+77010000001") => new()
    {
        TablesId = table.ToString(), Section = "VIP", CustomerName = "Test", CustomerPhone = phone,
        ScheduledAt = BookingRules.Wire(start), RemindBeforeHour = true
    };

    [Theory]
    [InlineData(26, "02:00")]
    [InlineData(30, "03:00")]
    [InlineData(31, "03:00")]
    public void WeeklyHoursIncludeMidnightOnTheFollowingCalendarDay(int day, string close)
    {
        var org = Organization();
        var date = new DateOnly(2026, 10, day);
        var now = date.ToDateTime(new TimeOnly(10, 0));
        Assert.Equal(close, BookingRules.HoursForDay(org, date)!.EndTime);
        var slots = BookingRules.Slots(date, org, now);
        Assert.Equal(13, slots.Count);
        Assert.Equal(date.ToDateTime(new TimeOnly(12, 0)), slots[0]);
        Assert.Equal(date.AddDays(1).ToDateTime(TimeOnly.MinValue), slots[^1]);
        BookingRules.ValidateStart(slots[^1], org, now);
        Assert.Throws<BookingException>(() => BookingRules.ValidateStart(slots[^1].AddHours(1), org, now));
    }

    [Fact]
    public void MidnightAtTheEndOfTheSeventhOpeningDayRemainsBookable()
    {
        var org = Organization();
        var now = new DateTime(2026, 10, 26, 10, 0, 0);
        var openingDay = DateOnly.FromDateTime(now).AddDays(6);
        var midnight = openingDay.AddDays(1).ToDateTime(TimeOnly.MinValue);
        BookingRules.ValidateStart(midnight, org, now);
        Assert.Throws<BookingException>(() => BookingRules.ValidateStart(midnight.AddDays(1), org, now));
    }

    [Fact]
    public void VisitMustFitWeeklyClosingAndMissingWeekdaysAreClosed()
    {
        var org = Organization();
        org.BookingTime.ReservationDeadline = "02:00";
        var monday = new DateOnly(2026, 10, 26);
        var friday = monday.AddDays(4);
        Assert.DoesNotContain(monday.AddDays(1).ToDateTime(new TimeOnly(2, 0)), BookingRules.Slots(monday, org, monday.ToDateTime(new TimeOnly(10, 0))));
        Assert.Contains(friday.AddDays(1).ToDateTime(new TimeOnly(2, 0)), BookingRules.Slots(friday, org, friday.ToDateTime(new TimeOnly(10, 0))));
        org.OpeningHours.RemoveAll(hours => hours.Day == DayOfWeek.Monday);
        Assert.Empty(BookingRules.Slots(monday, org, monday.ToDateTime(new TimeOnly(10, 0))));
    }

    [Fact]
    public void PublicBookingConfigPublishesOneHourAndMidnightWhileOtherTenantsRetainDefaults()
    {
        var org = Organization();
        org.BookingEnabled = true;
        var config = PublicTenantConfigResponse.FromOrganization(org);
        Assert.Equal(60, config.ReservationDurationMinutes);
        Assert.Equal("12:00", config.BookingTime.AvailableTimeSlots[0]);
        Assert.Equal("00:00", config.BookingTime.AvailableTimeSlots[^1]);
        Assert.DoesNotContain("01:00", config.BookingTime.AvailableTimeSlots);
        Assert.Equal(180, new OrganizationOptions().ReservationDurationMinutes);
        Assert.False(org.NotificationsEnabled);
        Assert.False(Organization().BookingEnabled);
    }

    [Fact]
    public async Task VipRoomIsExclusiveForOneHourAndAdjacentVisitsAreAllowed()
    {
        using var db = await DatabaseWithInventory();
        var org = Organization();
        var repo = Repository(db, org);
        var result = await repo.BookAsync(Request(28, Start), Start);
        Assert.Equal(60, result.DurationMinutes);
        var conflict = await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(Request(28, Start, "+77010000002"), Start));
        Assert.Equal("TABLE_TAKEN", conflict.Code);
        Assert.True(await repo.IsReservationTakenAsync("28", Start.AddMinutes(30)));
        Assert.False(await repo.IsReservationTakenAsync("29", Start));
        await repo.BookAsync(Request(28, Start.AddHours(1), "+77010000002"), Start.AddHours(1));
        var rows = (await db.QueryAsync("SELECT reserved_at,ends_at,remind_before_hour FROM secret_veil_table_reservations ORDER BY reserved_at")).Rows;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(TimeSpan.FromHours(1), BookingRules.Read(row.GetString("ends_at")) - BookingRules.Read(row.GetString("reserved_at"))));
        Assert.All(rows, row => Assert.False(row.GetBoolean("remind_before_hour")));
    }

    [Fact]
    public async Task StoredVisitEndDoesNotChangeWhenTheConfiguredDurationChanges()
    {
        using var db = await DatabaseWithInventory();
        var org = Organization();
        var repo = Repository(db, org);
        await repo.BookAsync(Request(1, Start), Start);
        org.ReservationDurationMinutes = 120;
        var row = Assert.Single(await repo.GetReservationsAsync(DateOnly.FromDateTime(Start)));
        Assert.Equal(BookingRules.Wire(Start.AddHours(1)), row.EndsAt);
        Assert.False(await repo.IsReservationTakenAsync("1", Start.AddHours(1)));
    }

    [Fact]
    public async Task MidnightBookingStoresItsEndAtOneAndRejectsLaterArrivals()
    {
        using var db = await DatabaseWithInventory();
        var org = Organization();
        var repo = Repository(db, org);
        var midnight = Start.Date.AddDays(1);
        await repo.BookAsync(Request(29, midnight), midnight);
        var row = Assert.Single(await repo.GetReservationsAsync(DateOnly.FromDateTime(midnight)));
        Assert.Equal(BookingRules.Wire(midnight.AddHours(1)), row.EndsAt);
        await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(Request(1, midnight.AddHours(1)), midnight.AddHours(1)));
    }

    [Fact]
    public async Task ConcurrentRequestsCannotShareAVipRoom()
    {
        using var db = await DatabaseWithInventory();
        var org = Organization();
        async Task<bool> Book(string phone)
        {
            try { await Repository(db, org).BookAsync(Request(28, Start, phone), Start); return true; }
            catch (BookingException ex) when (ex.Code == "TABLE_TAKEN") { return false; }
        }
        var results = await Task.WhenAll(Book("+77010000001"), Book("+77010000002"));
        Assert.Single(results, success => success);
    }

    [Fact]
    public async Task ControllerCommitsBookingWithoutCallingNotificationsBeforeRelease()
    {
        using var db = await DatabaseWithInventory();
        var org = Organization();
        var notifier = new RejectNotifications();
        var controller = new ReservationsController(Repository(db, org), notifier, Tenant(org),
            new BusinessTypeStrategyResolver([new RestaurantStrategy()]), NullLogger<ReservationsController>.Instance);
        var result = Assert.IsType<OkObjectResult>(await controller.CreateReservation(Request(28, Start), default));
        Assert.False(notifier.Called);
        Assert.Contains("\"whatsAppSent\":false", JsonSerializer.Serialize(result.Value));
        Assert.Single((await db.QueryAsync("SELECT id FROM secret_veil_table_reservations")).Rows);
    }

    private sealed class RejectNotifications : IReservationNotificationService
    {
        public bool Called { get; private set; }
        public Task<ReservationNotificationResult> SendReservationNotificationsAsync(ReservationInfo reservation, DateTime dateTime, string label, CancellationToken ct)
        { Called = true; throw new InvalidOperationException("Notifications must stay disabled."); }
        public Task<bool> SendReminderAsync(string reservationId, ReservationInfo reservation, DateTime dateTime, CancellationToken ct)
        { Called = true; throw new InvalidOperationException("Notifications must stay disabled."); }
    }
}
