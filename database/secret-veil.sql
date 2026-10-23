-- Run manually in the EXISTING shared Turso database.
-- Creates only Secret Veil tables. No credentials belong in this file.
-- Mirrors the inspected restaurant columns, including ends_at and timestamps.
-- Inventory stays empty until capacities are confirmed; no placeholder rows.
-- POS remains disabled: the existing restaurant_booking_rows references
-- TheTochka's table_reservations, not Secret Veil's reservations.
PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;

CREATE TABLE IF NOT EXISTS secret_veil_tables (
    id INTEGER PRIMARY KEY,
    type TEXT NOT NULL,
    capacity INTEGER NOT NULL CHECK (capacity > 0),
    status TEXT NOT NULL DEFAULT 'available' CHECK (status IN ('available', 'unavailable')),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS secret_veil_table_reservations (
    id TEXT PRIMARY KEY,
    table_id INTEGER NOT NULL REFERENCES secret_veil_tables(id),
    customer_name TEXT NOT NULL,
    customer_phone TEXT NOT NULL,
    reserved_at TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    remind_before_hour INTEGER NOT NULL DEFAULT 0 CHECK (remind_before_hour IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ends_at TEXT
);

CREATE TABLE IF NOT EXISTS secret_veil_reservation_reminders (
    reservation_id TEXT PRIMARY KEY REFERENCES secret_veil_table_reservations(id) ON DELETE CASCADE,
    sent_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_secret_veil_reservations_window
    ON secret_veil_table_reservations(table_id, status, reserved_at);
CREATE INDEX IF NOT EXISTS ix_secret_veil_reservations_phone
    ON secret_veil_table_reservations(customer_phone, status);

COMMIT;
