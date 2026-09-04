namespace table_reservations.Data
{
    /// <summary>
    /// Creates the reservation schema on the shared Turso database. Idempotent:
    /// safe to run on every application start.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private static readonly string[] Statements =
        {
            """
            CREATE TABLE IF NOT EXISTS tables (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                organization_id TEXT NOT NULL,
                table_number INTEGER NOT NULL,
                table_type TEXT NOT NULL DEFAULT '',
                seats INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            )
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_tables_org_number
                ON tables (organization_id, table_number)
            """,
            """
            CREATE TABLE IF NOT EXISTS reservations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                organization_id TEXT NOT NULL,
                table_ids TEXT NOT NULL DEFAULT '',
                customer_name TEXT NOT NULL DEFAULT '',
                customer_phone TEXT NOT NULL DEFAULT '',
                scheduled_at TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT '',
                remind_before_hour INTEGER NOT NULL DEFAULT 0,
                reminder_sent INTEGER NOT NULL DEFAULT 0,
                plate_number TEXT NOT NULL DEFAULT '',
                wash_service_type TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            )
            """,
            """
            CREATE INDEX IF NOT EXISTS ix_reservations_org_scheduled
                ON reservations (organization_id, scheduled_at)
            """,
            """
            CREATE INDEX IF NOT EXISTS ix_reservations_org_phone
                ON reservations (organization_id, customer_phone)
            """
        };

        private readonly ITursoClient _client;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(ITursoClient client, ILogger<DatabaseInitializer> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await _client.ExecuteBatchAsync(Statements, ct);
            _logger.LogInformation("Turso schema is up to date.");
        }
    }
}
