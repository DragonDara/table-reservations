-- Run after secret-veil.sql in the same shared Turso database.
-- Confirmed maximum guest counts for the main hall only.
-- Re-running adds missing tables without changing existing inventory or bookings.
BEGIN IMMEDIATE;

-- Small round tables: up to 4 guests.
INSERT INTO secret_veil_tables (id, type, capacity) VALUES
    (12, 'Обычный', 4),
    (13, 'Обычный', 4),
    (14, 'Обычный', 4),
    (15, 'Обычный', 4),
    (16, 'Обычный', 4),
    (17, 'Обычный', 4),
    (18, 'Обычный', 4),
    (19, 'Обычный', 4),
    (20, 'Обычный', 4),
    (21, 'Обычный', 4),
    (22, 'Обычный', 4),
    (23, 'Обычный', 4)
ON CONFLICT(id) DO NOTHING;

-- Larger rectangular tables: up to 6 guests.
INSERT INTO secret_veil_tables (id, type, capacity) VALUES
    (1, 'Обычный', 6),
    (2, 'Обычный', 6),
    (3, 'Обычный', 6),
    (4, 'Обычный', 6),
    (5, 'Обычный', 6),
    (6, 'Обычный', 6),
    (7, 'Обычный', 6),
    (8, 'Обычный', 6),
    (9, 'Обычный', 6),
    (10, 'Обычный', 6),
    (11, 'Обычный', 6),
    (24, 'Обычный', 6),
    (25, 'Обычный', 6)
ON CONFLICT(id) DO NOTHING;

-- VIP 28/29 await confirmed capacities and whole-room booking rules.
-- Balcony and terrace are out of season and are not inserted.
COMMIT;
