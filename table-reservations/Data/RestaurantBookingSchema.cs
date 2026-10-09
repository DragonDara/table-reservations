namespace table_reservations.Data;

public static class RestaurantBookingSchema
{
    public static readonly string[] Statements = [
        """
        CREATE TABLE IF NOT EXISTS restaurant_booking_operations (
            id TEXT PRIMARY KEY, organization_id TEXT NOT NULL, idempotency_key TEXT NOT NULL,
            request_hash TEXT NOT NULL, request_json TEXT NOT NULL, scheduled_at TEXT NOT NULL,
            state TEXT NOT NULL, correlation_id TEXT,
            lease_token TEXT, lease_until TEXT, next_attempt_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            attempts INTEGER NOT NULL DEFAULT 0, notification_claimed INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(organization_id,idempotency_key))
        """,
        """
        CREATE TABLE IF NOT EXISTS restaurant_booking_rows (
            operation_id TEXT NOT NULL REFERENCES restaurant_booking_operations(id),
            reservation_id TEXT PRIMARY KEY REFERENCES table_reservations(id))
        """,
        "CREATE INDEX IF NOT EXISTS ix_restaurant_operations_pending ON restaurant_booking_operations(organization_id,state,next_attempt_at)",
        "CREATE INDEX IF NOT EXISTS ix_restaurant_booking_rows_operation ON restaurant_booking_rows(operation_id)"
    ];
}