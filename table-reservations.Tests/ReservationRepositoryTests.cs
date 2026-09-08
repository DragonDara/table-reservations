using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests;

/// <summary>Runs the production repository SQL and migration against real, isolated SQLite.</summary>
public class ReservationRepositoryTests
{
    private sealed class Database : ITursoClient, IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly SemaphoreSlim gate = new(1);
        public Database()
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys=ON;
                CREATE TABLE tables(id INTEGER PRIMARY KEY,type TEXT NOT NULL,capacity INTEGER NOT NULL,status TEXT DEFAULT 'available');
                CREATE TABLE table_reservations(id TEXT PRIMARY KEY,table_id INTEGER REFERENCES tables(id),customer_name TEXT NOT NULL,customer_phone TEXT NOT NULL,reserved_at TEXT NOT NULL,status TEXT DEFAULT 'pending',remind_before_hour INTEGER DEFAULT 1);
                CREATE TABLE carwash_boxes(id TEXT PRIMARY KEY,name TEXT,is_active INTEGER DEFAULT 1);
                CREATE TABLE vehicle_categories(id TEXT PRIMARY KEY,name TEXT);
                CREATE TABLE carwash_services(id TEXT PRIMARY KEY,name TEXT,is_package INTEGER DEFAULT 0,is_active INTEGER DEFAULT 1);
                CREATE TABLE carwash_service_prices(service_id TEXT REFERENCES carwash_services(id),vehicle_category_id TEXT REFERENCES vehicle_categories(id),price_minor INTEGER NOT NULL,duration_minutes INTEGER,PRIMARY KEY(service_id,vehicle_category_id));
                CREATE TABLE carwash_package_items(package_service_id TEXT,included_service_id TEXT);
                CREATE TABLE box_reservations(
                    id TEXT PRIMARY KEY,box_id TEXT NOT NULL REFERENCES carwash_boxes(id),
                    vehicle_category_id TEXT NOT NULL REFERENCES vehicle_categories(id),
                    plate_number TEXT,customer_phone TEXT NOT NULL,customer_name TEXT,
                    scheduled_at TEXT NOT NULL,ends_at TEXT NOT NULL CHECK(ends_at>scheduled_at),
                    status TEXT DEFAULT 'pending' CHECK(status IN('pending','confirmed','in_progress','completed','cancelled','no_show')),
                    surcharge_percent INTEGER DEFAULT 0,total_minor INTEGER NOT NULL CHECK(total_minor>=0),
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,updated_at TEXT DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE box_reservation_services(reservation_id TEXT REFERENCES box_reservations(id),service_id TEXT REFERENCES carwash_services(id),service_name TEXT NOT NULL,unit_price_kzt INTEGER NOT NULL,duration_minutes INTEGER NOT NULL,PRIMARY KEY(reservation_id,service_id));
                INSERT INTO tables(id,type,capacity) VALUES(1,'Обычный',4),(2,'VIP',6);
                INSERT INTO carwash_boxes(id,name) VALUES('box_1','Box 1');
                INSERT INTO vehicle_categories VALUES('car','Car'),('minivan','Minivan');
                INSERT INTO carwash_services(id,name,is_package) VALUES
                    ('body_wash','Exterior',0),('interior_polish','Interior',0),('tire_blackening','Tires',0),
                    ('complex_wash','Full package',1),('engine_wash','Engine',0),('paper_mat','Paper mat',0);
                INSERT INTO carwash_service_prices(service_id,vehicle_category_id,price_minor) VALUES
                    ('body_wash','car',2500),('interior_polish','car',1500),('tire_blackening','car',500),
                    ('complex_wash','car',4000),('engine_wash','car',1200),('paper_mat','car',100),
                    ('complex_wash','minivan',5500);
                INSERT INTO carwash_package_items VALUES('complex_wash','body_wash'),('complex_wash','interior_polish'),('complex_wash','tire_blackening'),('complex_wash','paper_mat');
                """;
            cmd.ExecuteNonQuery();
        }
        public async Task<TursoResultSet> QueryAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default)
        {
            using var cmd = connection.CreateCommand();
            // The real HTTP API binds anonymous ? parameters positionally.
            var index = 0;
            cmd.CommandText = System.Text.RegularExpressions.Regex.Replace(sql, @"\?", _ => "$p" + index++);
            for (var i = 0; i < (args?.Count ?? 0); i++) cmd.Parameters.AddWithValue("$p" + i, args![i] ?? DBNull.Value);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<TursoRow>();
            while (await reader.ReadAsync(ct))
                rows.Add(new(Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName,
                    i => reader.IsDBNull(i) ? null : reader.GetValue(i), StringComparer.OrdinalIgnoreCase)));
            return new() { Rows = rows, AffectedRowCount = reader.RecordsAffected };
        }
        public async Task<long> ExecuteAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default)
        {
            await QueryAsync(sql, args, ct); return 0;
        }
        public async Task ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default)
        {
            foreach (var sql in statements) await QueryAsync(sql, ct: ct);
        }
        public async Task<IReadOnlyList<TursoResultSet>> QueryBatchAsync(IReadOnlyList<TursoStatement> statements, CancellationToken ct = default)
        {
            var result = new List<TursoResultSet>();
            foreach (var statement in statements) result.Add(await QueryAsync(statement.Sql, statement.Args, ct));
            return result;
        }
        public async Task<T> TransactionAsync<T>(Func<ITursoClient, CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            await gate.WaitAsync(ct);
            try
            {
                await ExecuteAsync("BEGIN IMMEDIATE", ct: ct);
                try { var result = await action(this, ct); await ExecuteAsync("COMMIT", ct: ct); return result; }
                catch { await ExecuteAsync("ROLLBACK"); throw; }
            }
            finally { gate.Release(); }
        }
        public void Dispose() { connection.Dispose(); gate.Dispose(); }
    }
    private static DatabaseInitializer Initializer(Database db) => new(db, NullLogger<DatabaseInitializer>.Instance);
    private static TursoReservationRepository Repository(Database db, bool carwash = true, string? id = null)
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions {
            Id = id ?? (carwash ? "thetochka-carwasher" : "thetochka"),
            BusinessType = carwash ? BusinessType.CarWash : BusinessType.Restaurant,
            BookingTime = new() { StartTime = carwash ? "08:00" : "12:00", EndTime = carwash ? "20:00" : "04:00", SlotDurationMinutes = 60 }
        });
        return new(db, tenant, Options.Create(new TursoOptions()));
    }
    private static DateTime Start(int hour = 10) => ReservationDateTime.KazakhstanNow().Date.AddDays(2).AddHours(hour);
    private static ReservationInfo Request(string phone = "+77010000001", params string[] ids) => new() {
        CustomerName = "Test", CustomerPhone = phone, PlateNumber = phone, VehicleCategoryId = "car",
        ServiceIds = ids.Length > 0 ? ids : ["complex_wash"], ScheduledAt = BookingRules.Wire(Start()), TablesId = "1"
    };

    [Fact]
    public async Task ReservationListUsesLocalDayOverlapAndActiveStatusesWithoutCustomerData()
    {
        using var db = new Database();
        var day = new DateOnly(2026, 9, 8);
        async Task Add(string id, string local, string status = "confirmed", int table = 1) =>
            await db.ExecuteAsync("""
                INSERT INTO table_reservations(id,table_id,customer_name,customer_phone,reserved_at,status)
                VALUES(?,?,'Private name','Private phone',?,?)
                """, [id, table, BookingRules.Store(DateTime.Parse(local)), status]);

        await Add("late", "2026-09-08T23:00:00", "pending", 2);
        await Add("midnight", "2026-09-08T00:00:00", "in_progress");
        await Add("carry", "2026-09-07T23:00:00");
        await Add("ends-at-midnight", "2026-09-07T21:00:00");
        await Add("next-day", "2026-09-09T00:00:00");
        foreach (var status in new[] { "cancelled", "completed", "no_show" })
            await Add(status, "2026-09-08T12:00:00", status);

        var result = await Repository(db, carwash: false).GetReservationsAsync(day);
        Assert.Equal(["2026-09-07T23:00", "2026-09-08T00:00", "2026-09-08T23:00"], result.Select(r => r.ScheduledAt));
        Assert.Equal(["2026-09-08T02:00", "2026-09-08T03:00", "2026-09-09T02:00"], result.Select(r => r.EndsAt));
        Assert.Equal(["1", "1", "2"], result.Select(r => r.TablesId));
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(result));
        Assert.Empty(await Repository(db, carwash: false).GetReservationsAsync(day.AddDays(10)));
        await Assert.ThrowsAsync<BookingException>(() => Repository(db, carwash: false, id: "other").GetReservationsAsync(day));
    }

    [Fact]
    public async Task CarwashReservationListUsesStoredDurationAndServicesAndKeepsTenantsSeparate()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await db.ExecuteAsync("""
            INSERT INTO box_reservations(id,box_id,vehicle_category_id,plate_number,customer_phone,customer_name,scheduled_at,ends_at,total_minor,services_json,status)
            VALUES('visit','box_1','car','Private plate','Private phone','Private name',
                '2026-09-07 18:30:00','2026-09-07 20:00:00',0,'["body_wash","interior_polish"]','confirmed'),
                ('cancelled','box_1','car','','','',
                '2026-09-07 18:30:00','2026-09-07 20:00:00',0,'[]','cancelled'),
                ('ends-at-midnight','box_1','car','','','',
                '2026-09-07 18:00:00','2026-09-07 19:00:00',0,'[]','confirmed'),
                ('next-day','box_1','car','','','',
                '2026-09-08 19:00:00','2026-09-08 20:00:00',0,'[]','confirmed')
            """);
        var date = new DateOnly(2026, 9, 8);
        var items = await Repository(db).GetReservationsAsync(date);
        var item = Assert.Single(items);
        Assert.Equal("2026-09-07T23:30", item.ScheduledAt);
        Assert.Equal("2026-09-08T01:00", item.EndsAt);
        Assert.Equal("box_1", item.BoxId);
        Assert.Equal("Exterior, Interior", item.WashServiceType);
        Assert.Empty(item.TablesId);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(items));
        Assert.Empty(await Repository(db, carwash: false).GetReservationsAsync(date));
        await Assert.ThrowsAsync<BookingException>(() => Repository(db, id: "other").GetReservationsAsync(date));
    }

    [Fact]
    public async Task MigrationIsAdditiveAndIdempotent()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await Initializer(db).MigrateAsync();
        Assert.Equal(2, (await db.QueryAsync("SELECT id FROM tables")).Rows.Count);
        Assert.Contains((await db.QueryAsync("PRAGMA table_info(box_reservations)")).Rows, row => row.GetString("name") == "services_json");
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync("""
            INSERT INTO box_reservations(id,box_id,vehicle_category_id,customer_phone,scheduled_at,ends_at,total_minor,services_json)
            VALUES('invalid','box_1','car','x','2026-09-07 10:00:00','2026-09-07 11:00:00',0,'{}')
            """));
    }

    [Fact]
    public async Task MigrationBackfillsLegacyServicesWithoutChangingReservations()
    {
        using var db = new Database();
        await db.ExecuteAsync("""
            INSERT INTO box_reservations(id,box_id,vehicle_category_id,customer_phone,scheduled_at,ends_at,total_minor)
            VALUES('old','box_1','car','x','2026-09-07 10:00:00','2026-09-07 11:00:00',250000)
            """);
        await db.ExecuteAsync("INSERT INTO box_reservation_services VALUES('old','body_wash','Exterior',2500,50)");
        await Initializer(db).MigrateAsync();
        var row = Assert.Single((await db.QueryAsync("SELECT * FROM box_reservations")).Rows);
        Assert.Equal(new[] { "body_wash" }, JsonSerializer.Deserialize<string[]>(row.GetString("services_json")));
        Assert.Equal(250000, row.GetInt64("total_minor"));
    }

    [Fact]
    public async Task BookingStoresServiceArrayAuthoritativeTotalAndFullDuration()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var request = Request("+77010000001", "body_wash", "interior_polish");
        request.WashServiceType = "tampered client label";
        var result = await Repository(db).BookAsync(request, Start());
        Assert.Equal(50, result.DurationMinutes);
        Assert.Equal(4000, result.TotalKzt);
        var row = Assert.Single((await db.QueryAsync("SELECT * FROM box_reservations")).Rows);
        Assert.Equal(new[] { "body_wash", "interior_polish" }, JsonSerializer.Deserialize<string[]>(row.GetString("services_json")));
        Assert.Equal(400000, row.GetInt64("total_minor"));
        Assert.Equal(BookingRules.Store(Start().AddMinutes(50)), row.GetString("ends_at"));
        Assert.Equal("Exterior, Interior", request.WashServiceType);
        Assert.Empty((await db.QueryAsync("SELECT * FROM table_reservations")).Rows);
    }

    [Fact]
    public async Task OverlapUsesWholeNinetyMinutesAndAdjacentSlotStaysAvailable()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var repo = Repository(db);
        await repo.BookAsync(Request(), Start());
        var availability = await repo.GetCarWashAvailabilityAsync(DateOnly.FromDateTime(Start()), new("car", ["complex_wash"]));
        Assert.DoesNotContain(BookingRules.Wire(Start()), availability.Slots);
        Assert.DoesNotContain(BookingRules.Wire(Start().AddHours(1)), availability.Slots);
        Assert.Contains(BookingRules.Wire(Start().AddHours(2)), availability.Slots);
        Assert.DoesNotContain(BookingRules.Wire(Start(19)), availability.Slots);
        Assert.Equal("SLOT_TAKEN", (await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(Request("+77010000002"), Start().AddHours(1)))).Code);
        await repo.BookAsync(Request("+77010000002"), Start().AddHours(2));
    }

    [Fact]
    public async Task MultipleBoxesAllowParallelVisitsButCapacityIsFinite()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await db.ExecuteAsync("INSERT INTO carwash_boxes(id,name) VALUES('box_2','Box 2')");
        var repo = Repository(db);
        var first = await repo.BookAsync(Request(), Start());
        var second = await repo.BookAsync(Request("+77010000002"), Start());
        Assert.NotEqual(first.BoxId, second.BoxId);
        Assert.Equal("SLOT_TAKEN", (await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(Request("+77010000003"), Start()))).Code);
    }

    [Fact]
    public async Task CompetingRequestsCannotDoubleBookOneBox()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        async Task<bool> Attempt(string phone)
        {
            try { await Repository(db).BookAsync(Request(phone), Start()); return true; }
            catch (BookingException ex) when (ex.Code == "SLOT_TAKEN") { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt("+77010000001")), Task.Run(() => Attempt("+77010000002")));
        Assert.Single(results, success => success);
        Assert.Single((await db.QueryAsync("SELECT id FROM box_reservations")).Rows);
    }

    [Fact]
    public async Task FailedReplacementRollsBackCancellation()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var repo = Repository(db);
        var first = await repo.BookAsync(Request(), Start());
        await db.ExecuteAsync("CREATE TRIGGER fail_booking BEFORE INSERT ON box_reservations BEGIN SELECT RAISE(ABORT,'test failure'); END");
        var replacement = Request();
        replacement.Overwrite = true;
        await Assert.ThrowsAsync<SqliteException>(() => repo.BookAsync(replacement, Start(12)));
        var row = Assert.Single((await db.QueryAsync("SELECT * FROM box_reservations")).Rows);
        Assert.Equal(first.Id, row.GetString("id"));
        Assert.Equal("confirmed", row.GetString("status"));
    }

    [Fact]
    public async Task ReplacementPreservesHistoryAndNormalizesPhone()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var repo = Repository(db);
        await repo.BookAsync(Request("+77010000001"), Start());
        var replacement = Request("8 (701) 000-00-01");
        Assert.Equal("EXISTING_RESERVATION", (await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(replacement, Start(12)))).Code);
        replacement.Overwrite = true;
        Assert.True((await repo.BookAsync(replacement, Start(12))).Overwritten);
        Assert.Single((await db.QueryAsync("SELECT * FROM box_reservations WHERE status='confirmed'")).Rows);
        Assert.Single((await db.QueryAsync("SELECT * FROM box_reservations WHERE status='cancelled'")).Rows);
    }

    [Fact]
    public async Task RestaurantUsesOnlyLoungeTablesAndSupportsMultipleTables()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var repo = Repository(db, false);
        var request = Request();
        request.TablesId = "1,2";
        await repo.BookAsync(request, Start(12));
        Assert.Equal(2, (await db.QueryAsync("SELECT id FROM table_reservations")).Rows.Count);
        Assert.Empty((await db.QueryAsync("SELECT id FROM box_reservations")).Rows);
        Assert.DoesNotContain(Start(13), await repo.GetAvailableSlotsAsync(DateOnly.FromDateTime(Start()), ReservationDateTime.KazakhstanNow()));
        Assert.Contains(Start(15), await repo.GetAvailableSlotsAsync(DateOnly.FromDateTime(Start()), ReservationDateTime.KazakhstanNow()));
    }

    [Fact]
    public async Task UnmappedTenantCannotAccessEitherBusiness()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        var repo = Repository(db, id: "third-tenant");
        Assert.Equal(404, (await Assert.ThrowsAsync<BookingException>(() => repo.GetCarWashCatalogAsync())).Status);
        await Assert.ThrowsAsync<BookingException>(() => Repository(db, false).GetCarWashCatalogAsync());
        await Assert.ThrowsAsync<BookingException>(() => Repository(db).GetTablesAsync());
    }

    [Fact]
    public async Task InactiveBoxAndUnavailableServiceFailClosed()
    {
        using var db = new Database();
        await Initializer(db).MigrateAsync();
        await db.ExecuteAsync("UPDATE carwash_boxes SET is_active=0");
        var repo = Repository(db);
        Assert.Empty((await repo.GetCarWashAvailabilityAsync(DateOnly.FromDateTime(Start()), new("car", ["complex_wash"]))).Slots);
        await Assert.ThrowsAsync<BookingException>(() => repo.BookAsync(Request(), Start()));
        await db.ExecuteAsync("UPDATE carwash_services SET is_active=0 WHERE id='complex_wash'");
        await Assert.ThrowsAsync<BookingException>(() => repo.GetCarWashAvailabilityAsync(DateOnly.FromDateTime(Start()), new("car", ["complex_wash"])));
    }
}
