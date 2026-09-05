namespace table_reservations.Data;

/// <summary>Startup validates only. Run explicitly with --migrate for additive schema changes.</summary>
public sealed class DatabaseInitializer(ITursoClient client, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await client.QueryBatchAsync([
            new("SELECT id,type,capacity,status FROM tables LIMIT 0"),
            new("SELECT id,table_id,customer_name,customer_phone,reserved_at,status,remind_before_hour FROM table_reservations LIMIT 0"),
            new("SELECT id,box_id,vehicle_category_id,plate_number,customer_phone,customer_name,scheduled_at,ends_at,status,total_minor,services_json FROM box_reservations LIMIT 0"),
            new("SELECT reservation_id,sent_at FROM reservation_reminders LIMIT 0"),
            new("SELECT id,name,is_active FROM carwash_boxes LIMIT 0"),
            new("SELECT id,name FROM vehicle_categories LIMIT 0"),
            new("SELECT id,name,is_package,is_active FROM carwash_services LIMIT 0"),
            new("SELECT service_id,vehicle_category_id,price_minor,duration_minutes FROM carwash_service_prices LIMIT 0"),
            new("SELECT package_service_id,included_service_id FROM carwash_package_items LIMIT 0")
        ], ct);
        logger.LogInformation("Turso schema validated.");
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await client.TransactionAsync(async (db, token) =>
        {
            var columns = await db.QueryAsync("PRAGMA table_info(box_reservations)", ct: token);
            if (columns.Rows.Count == 0) throw new InvalidOperationException("Create the carwash catalog and reservation tables before migrating.");
            var statements = new List<string>();
            if (!columns.Rows.Any(r => r.GetString("name") == "services_json"))
                statements.Add("ALTER TABLE box_reservations ADD COLUMN services_json TEXT NOT NULL DEFAULT '[]' CHECK (json_valid(services_json) AND json_type(services_json) = 'array')");
            statements.AddRange([
                "CREATE TABLE IF NOT EXISTS reservation_reminders (reservation_id TEXT PRIMARY KEY REFERENCES table_reservations(id) ON DELETE CASCADE, sent_at TEXT NOT NULL)",
                "CREATE INDEX IF NOT EXISTS ix_box_reservations_window ON box_reservations(box_id,status,scheduled_at,ends_at)",
                "CREATE INDEX IF NOT EXISTS ix_table_reservations_window ON table_reservations(table_id,status,reserved_at)",
                "CREATE INDEX IF NOT EXISTS ix_box_reservations_phone ON box_reservations(customer_phone,status)",
                "CREATE INDEX IF NOT EXISTS ix_table_reservations_phone ON table_reservations(customer_phone,status)"
            ]);
            var legacy = await db.QueryAsync("SELECT name FROM sqlite_master WHERE type='table' AND name='box_reservation_services'", ct: token);
            if (legacy.Rows.Count > 0)
                statements.Add("""
                    UPDATE box_reservations SET services_json = (
                        SELECT json_group_array(service_id) FROM (
                            SELECT service_id FROM box_reservation_services
                            WHERE reservation_id = box_reservations.id ORDER BY service_id
                        )
                    ) WHERE services_json = '[]' AND EXISTS (
                        SELECT 1 FROM box_reservation_services WHERE reservation_id = box_reservations.id
                    )
                    """);
            await db.ExecuteBatchAsync(statements, token);
            return true;
        }, ct);
        await InitializeAsync(ct);
        logger.LogInformation("Additive Turso migration completed.");
    }
}
