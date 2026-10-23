using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;
using Database = table_reservations.Tests.ReservationRepositoryTests.Database;

namespace table_reservations.Tests;

public class SecretVeilStorageTests
{
    private static string Schema => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "secret-veil.sql"));
    private static OrganizationOptions Veil(bool enabled = true) => new()
    {
        Id = "secret-veil", BookingEnabled = enabled,
        RestaurantStorage = new() { TablePrefix = "secret_veil_" }
    };
    private static TursoReservationRepository Repository(Database db, bool veil)
    {
        var tenant = new TenantContext();
        tenant.Set(veil ? Veil() : new OrganizationOptions { Id = "thetochka" });
        return new(db, tenant, Options.Create(new TursoOptions()));
    }
    private static DatabaseInitializer Initializer(Database db, bool veilEnabled = false) =>
        new(db, NullLogger<DatabaseInitializer>.Instance,
            organizations: Options.Create(new OrganizationsOptions { Items = [Veil(veilEnabled)] }));
    private static DateTime Start => ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(14);
    private static ReservationInfo Request(string table, string phone = "+77010000001", bool overwrite = false) =>
        new() { TablesId = table, CustomerName = "Test", CustomerPhone = phone, Overwrite = overwrite, RemindBeforeHour = false };

    [Fact]
    public void ConfiguredTenantSelectsVeilTablesWithoutPublishingStorageConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tenant-settings.json")).Build();
        var organizations = configuration.GetSection(OrganizationsOptions.SectionName).Get<OrganizationsOptions>()!;
        var registry = new OrganizationRegistry(Options.Create(organizations));
        Assert.True(registry.TryGetById("secret-veil", out var org));
        Assert.Equal(new("secret_veil_tables", "secret_veil_table_reservations", "secret_veil_reservation_reminders"),
            RestaurantStorageTables.Resolve(org, new TursoOptions()));
        var publicConfig = PublicTenantConfigResponse.FromOrganization(org);
        Assert.False(publicConfig.BookingEnabled);
        Assert.DoesNotContain("RestaurantStorage", JsonSerializer.Serialize(publicConfig));
        Assert.DoesNotContain("secret_veil_", JsonSerializer.Serialize(publicConfig));
    }

    [Fact]
    public async Task ManualSchemaSeedsCurrentIndoorPositionsAndPreservesDataOnRepeat()
    {
        using var db = new Database();
        await db.ExecuteAsync("INSERT INTO table_reservations(id,table_id,customer_name,customer_phone,reserved_at) VALUES('existing',1,'Test','+77010000001','2026-10-23 10:00:00')");
        await db.ExecuteAsync(Schema);
        var tables = (await db.QueryAsync("SELECT * FROM secret_veil_tables ORDER BY id")).Rows;
        Assert.Equal(Enumerable.Range(1, 25).Concat([28, 29]), tables.Select(row => row.GetInt32("id")));
        Assert.All(tables, row => { Assert.Null(row["capacity"]); Assert.Equal("inactive", row.GetString("status")); });
        Assert.Equal(12, tables.Count(row => row.GetString("area") == "hall" && row.GetString("shape") == "round"));
        Assert.Equal(13, tables.Count(row => row.GetString("area") == "hall" && row.GetString("shape") == "rectangular"));
        Assert.Equal(2, tables.Count(row => row.GetString("area") == "vip" && row.GetString("type") == "VIP"));
        Assert.Empty(await Repository(db, true).GetTablesAsync(Start));

        await db.ExecuteAsync("UPDATE secret_veil_tables SET capacity=6,status='available' WHERE id=1");
        var booking = await Repository(db, true).BookAsync(Request("1"), Start);
        await db.ExecuteAsync(Schema);
        var retained = Assert.Single((await db.QueryAsync("SELECT capacity,status FROM secret_veil_tables WHERE id=1")).Rows);
        Assert.Equal(6, retained.GetInt32("capacity"));
        Assert.Equal("available", retained.GetString("status"));
        Assert.Single((await db.QueryAsync("SELECT id FROM secret_veil_table_reservations WHERE id=?", [booking.Id])).Rows);
        Assert.Single((await db.QueryAsync("SELECT id FROM table_reservations WHERE id='existing'")).Rows);
        Assert.Equal(2, (await db.QueryAsync("SELECT id FROM tables")).Rows.Count);
        Assert.Single((await db.QueryAsync("SELECT id FROM carwash_boxes")).Rows);
    }

    [Fact]
    public async Task ReadsSlotsBookingAndOverwriteStayInsideTheirRestaurant()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await db.ExecuteAsync(Schema);
        await db.ExecuteAsync("UPDATE secret_veil_tables SET capacity=6,status='available' WHERE id IN (1,2)");
        var veil = Repository(db, true);
        var tochka = Repository(db, false);
        var tochkaBooking = await tochka.BookAsync(Request("1"), Start);
        var veilBooking = await veil.BookAsync(Request("1"), Start);
        await veil.BookAsync(Request("2", "+77010000002"), Start);
        Assert.Equal(6, (await veil.GetTablesAsync(Start)).First().Seats);
        Assert.Equal(4, (await tochka.GetTablesAsync(Start)).First().Seats);
        var day = DateOnly.FromDateTime(Start);
        Assert.DoesNotContain(Start, await veil.GetAvailableSlotsAsync(day, ReservationDateTime.KazakhstanNow()));
        Assert.Contains(Start, await tochka.GetAvailableSlotsAsync(day, ReservationDateTime.KazakhstanNow()));
        Assert.Equal(2, (await veil.GetReservationsAsync(day)).Count);
        Assert.Single(await tochka.GetReservationsAsync(day));

        var replacement = await veil.BookAsync(Request("1", overwrite: true), Start.AddHours(4));
        Assert.True(replacement.Overwritten);
        Assert.Equal("cancelled", Assert.Single((await db.QueryAsync("SELECT status FROM secret_veil_table_reservations WHERE id=?", [veilBooking.Id])).Rows).GetString("status"));
        Assert.Equal("confirmed", Assert.Single((await db.QueryAsync("SELECT status FROM table_reservations WHERE id=?", [tochkaBooking.Id])).Rows).GetString("status"));
        Assert.False(await veil.IsReservationTakenAsync("1", Start));
        Assert.True(await tochka.IsReservationTakenAsync("1", Start));
        Assert.True(await veil.IsReservationTakenAsync("1", Start.AddHours(4)));
        Assert.Empty((await db.QueryAsync("SELECT id FROM box_reservations")).Rows);
        await Assert.ThrowsAsync<BookingException>(() => veil.GetCarWashCatalogAsync());
    }

    [Fact]
    public async Task ReminderCandidatesAndReceiptsStayIsolatedEvenWithIdenticalReservationIds()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await db.ExecuteAsync(Schema);
        foreach (var (prefix, name) in new[] { ("", "Tochka"), ("secret_veil_", "Veil") })
            await db.ExecuteAsync($"INSERT INTO {prefix}table_reservations(id,table_id,customer_name,customer_phone,reserved_at,status,remind_before_hour) VALUES('same-id',1,?,'+77010000001',?,'confirmed',1)",
                [name, BookingRules.Store(ReservationDateTime.KazakhstanNow().AddMinutes(30))]);
        var veil = Repository(db, true);
        var tochka = Repository(db, false);
        Assert.Equal("Veil", Assert.Single(await veil.GetReminderCandidatesAsync()).Reservation.CustomerName);
        Assert.Equal("Tochka", Assert.Single(await tochka.GetReminderCandidatesAsync()).Reservation.CustomerName);
        await veil.MarkReminderSentAsync("same-id", CancellationToken.None);
        Assert.Empty(await veil.GetReminderCandidatesAsync());
        Assert.Single(await tochka.GetReminderCandidatesAsync());
        Assert.Empty((await db.QueryAsync("SELECT reservation_id FROM reservation_reminders")).Rows);
        await tochka.MarkReminderSentAsync("same-id", CancellationToken.None);
        Assert.Empty(await tochka.GetReminderCandidatesAsync());
    }

    [Fact]
    public async Task UnknownCapacityCannotBeActivatedAndReservationsCannotReferenceAnotherRestaurantsTable()
    {
        using var db = new Database();
        await db.ExecuteAsync(Schema);
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync("UPDATE secret_veil_tables SET status='available' WHERE id=1"));
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync("UPDATE secret_veil_tables SET capacity=0 WHERE id=1"));
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync("INSERT INTO secret_veil_table_reservations(id,table_id,customer_name,customer_phone,reserved_at) VALUES('invalid',26,'Test','+77010000001','2026-10-23 10:00:00')"));
    }

    [Fact]
    public async Task StartupRequiresAdditionalSchemaOnlyWhenItsTenantIsEnabled()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        Assert.Empty((await db.QueryAsync("SELECT name FROM sqlite_master WHERE name='secret_veil_tables'")).Rows);
        await Initializer(db).InitializeAsync();
        await Assert.ThrowsAsync<SqliteException>(() => Initializer(db, true).InitializeAsync());
        await db.ExecuteAsync(Schema);
        await Initializer(db, true).InitializeAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret_veil")]
    [InlineData("secret-veil_")]
    [InlineData("secret_veil_; DROP TABLE tables;--")]
    [InlineData("sqlite_")]
    public void InvalidTablePrefixesAreRejectedBeforeSql(string prefix)
    {
        var org = Veil();
        org.RestaurantStorage!.TablePrefix = prefix;
        Assert.Throws<InvalidOperationException>(() => new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [org] })));
        Assert.Throws<InvalidOperationException>(() => RestaurantStorageTables.Resolve(org, new TursoOptions()));
    }

    [Fact]
    public void TwoTenantsCannotShareTheSameExplicitNamespace()
    {
        var other = Veil();
        other.Id = "another-restaurant";
        Assert.Throws<InvalidOperationException>(() => new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [Veil(), other] })));
        other.RestaurantStorage!.TablePrefix = "another_";
        other.BusinessType = BusinessType.CarWash;
        Assert.Throws<InvalidOperationException>(() => new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [other] })));
    }
}
