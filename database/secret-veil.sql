-- Run manually in the EXISTING shared Turso database.
-- Creates only Secret Veil tables. No credentials belong in this file.
-- Capacity is intentionally NULL and inventory inactive until venue confirmation.
PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;

CREATE TABLE IF NOT EXISTS secret_veil_tables (
    id INTEGER PRIMARY KEY CHECK (id > 0),
    type TEXT NOT NULL CHECK (type IN ('Обычный', 'VIP')),
    area TEXT NOT NULL CHECK (area IN ('hall', 'vip')),
    shape TEXT NOT NULL CHECK (shape IN ('round', 'rectangular')),
    capacity INTEGER CHECK (capacity IS NULL OR (typeof(capacity) = 'integer' AND capacity > 0)),
    status TEXT NOT NULL DEFAULT 'inactive' CHECK (status IN ('inactive', 'available')),
    CHECK (status <> 'available' OR capacity IS NOT NULL)
);

CREATE TABLE IF NOT EXISTS secret_veil_table_reservations (
    id TEXT PRIMARY KEY NOT NULL,
    table_id INTEGER NOT NULL REFERENCES secret_veil_tables(id) ON DELETE RESTRICT,
    customer_name TEXT NOT NULL,
    customer_phone TEXT NOT NULL,
    reserved_at TEXT NOT NULL CHECK (datetime(reserved_at) IS NOT NULL),
    status TEXT NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'confirmed', 'in_progress', 'completed', 'cancelled', 'no_show')),
    remind_before_hour INTEGER NOT NULL DEFAULT 0 CHECK (remind_before_hour IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS secret_veil_reservation_reminders (
    reservation_id TEXT PRIMARY KEY NOT NULL REFERENCES secret_veil_table_reservations(id) ON DELETE CASCADE,
    sent_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_secret_veil_reservations_window
    ON secret_veil_table_reservations(table_id, status, reserved_at);
CREATE INDEX IF NOT EXISTS ix_secret_veil_reservations_phone
    ON secret_veil_table_reservations(customer_phone, status);

-- IDs match the current picker: 25 hall tables and provisional VIP entries 28/29.
-- No balcony or terrace inventory. Re-running preserves existing capacities,
-- activation states, and bookings; it only inserts missing inventory entries.
INSERT INTO secret_veil_tables (id, type, area, shape) VALUES
    (1, 'Обычный', 'hall', 'rectangular'),
    (2, 'Обычный', 'hall', 'rectangular'),
    (3, 'Обычный', 'hall', 'rectangular'),
    (4, 'Обычный', 'hall', 'rectangular'),
    (5, 'Обычный', 'hall', 'rectangular'),
    (6, 'Обычный', 'hall', 'rectangular'),
    (7, 'Обычный', 'hall', 'rectangular'),
    (8, 'Обычный', 'hall', 'rectangular'),
    (9, 'Обычный', 'hall', 'rectangular'),
    (10, 'Обычный', 'hall', 'rectangular'),
    (11, 'Обычный', 'hall', 'rectangular'),
    (12, 'Обычный', 'hall', 'round'),
    (13, 'Обычный', 'hall', 'round'),
    (14, 'Обычный', 'hall', 'round'),
    (15, 'Обычный', 'hall', 'round'),
    (16, 'Обычный', 'hall', 'round'),
    (17, 'Обычный', 'hall', 'round'),
    (18, 'Обычный', 'hall', 'round'),
    (19, 'Обычный', 'hall', 'round'),
    (20, 'Обычный', 'hall', 'round'),
    (21, 'Обычный', 'hall', 'round'),
    (22, 'Обычный', 'hall', 'round'),
    (23, 'Обычный', 'hall', 'round'),
    (24, 'Обычный', 'hall', 'rectangular'),
    (25, 'Обычный', 'hall', 'rectangular'),
    (28, 'VIP', 'vip', 'round'),
    (29, 'VIP', 'vip', 'rectangular')
ON CONFLICT(id) DO NOTHING;

COMMIT;
