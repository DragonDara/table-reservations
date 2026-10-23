# Secret Veil

The public venue name is **The Veil**; its organization id and subdomain remain
`secret-veil`. The BRON header and shared restaurant structure are preserved.
The venue's own brand guide supplies its near-black/warm-white palette, amber
accents, Geist typography, original photographs, and keyhole framing. These
styles are scoped by `data-organization="secret-veil"` and do not apply to
other tenants. Brand assets and the font license are in
`my-site/public/tenants/secret-veil/`.

Secret Veil uses the existing restaurant page and native tenant API, Turso
repository, and tenant resolution middleware. No separate page or storage
implementation is needed.

Open `http://secret-veil.localhost:5173/` with the API running on port 5183.
`/secret-veil/` also selects the tenant on shared local hosts. Keep the private
Turso and integration settings in ignored local settings or environment variables.

`Organizations.Items[2].BookingEnabled` is false while the venue's inventory,
booking rules, and storage setup are pending. Booking endpoints return 503 and reminder
processing skips the venue. Public branding and the rating endpoint remain
available. Do not enable bookings until its Turso data is isolated from the
existing restaurant and the reservation rules and picker integration are ready.

The 2GIS score and review count are a saved snapshot. `Rating.Mode = Snapshot`
does not start a paid scraping job or require an Apify token.

## Opening hours

`Organizations.Items[2].OpeningHours` in `appsettings.json` stores the confirmed
weekly schedule in local Atyrau time: Monday–Thursday and Sunday 12:00–02:00,
Friday–Saturday 12:00–03:00. Each entry belongs to the day the venue opens;
closing is on the following calendar day. The public config exposes these
hours even while bookings are disabled, and the existing reservation section
shows them grouped by matching consecutive weekdays.

Opening hours are separate from `BookingTime`, which still controls reservation
start slots and currently supports one daily window. Confirm reservation cutoff,
visit duration, and VIP rules, then add weekday-aware booking validation before
enabling bookings for The Veil. The opening-hours display does not generate slots.

## Turso tables

Secret Veil shares the existing Turso connection but uses separate physical
tables, configured by `Organizations.Items[2].RestaurantStorage.TablePrefix`:

| Purpose | Table |
| --- | --- |
| Table inventory | `secret_veil_tables` |
| Reservations | `secret_veil_table_reservations` |
| Reminder receipts | `secret_veil_reservation_reminders` |

Run the complete [`database/secret-veil.sql`](../database/secret-veil.sql) script
manually in the existing shared Turso database. It creates these three tables,
foreign keys, and lookup indexes. It does not touch the existing restaurant or
carwash tables. Repeating it preserves inventory and reservations. It creates
missing tables only; it does not alter a namespace created with another schema.

The inventory uses the inspected restaurant structure: `id`, `type`, mandatory
positive `capacity`, `status` (`available` or `unavailable`), `created_at`, and
`updated_at`. Reservation columns include the nullable `ends_at` field present
in the current database. Reminder preference defaults to off for Veil; this
script does not enable notifications.

The schema script leaves inventory empty. Then run
[`database/secret-veil-tables.sql`](../database/secret-veil-tables.sql) to insert
the 25 confirmed hall tables: small round tables 12–23 accommodate up to 4
guests; larger rectangular tables 1–11 and 24–25 accommodate up to 6. Both use
the existing `Обычный` table type. Inserts use the default `available` status;
repeating the script adds missing rows without overwriting existing inventory
or reservations. These are maximum guest counts, not required party sizes.

The same script also inserts the smaller VIP room 28 for up to 6 guests and
larger VIP room 29 for up to 8, both with type `VIP`. Whole-room booking rules
still need confirmation. Select all INSERT statements together in the Turso
SQL Console; it handles the transaction automatically, without explicit BEGIN
or COMMIT statements. Balcony and terrace remain out of season.
Room/shape coordinates remain in the frontend floor-plan data.

The shared `telegram_groups` and `notification_deliveries` tables already scope
their records by `organization_id` and do not need copies for Veil. The shared
`restaurant_booking_operations` also has `organization_id`, but
`restaurant_booking_rows.reservation_id` references the legacy
`table_reservations`, so that link cannot store Veil's reservation IDs. Keep
`Pos.Enabled` false: adding `ends_at` does not connect the new namespace to the
iiko workflow. Its operation/row routing needs a separate integration change.

The backend routes inventory, availability, reservation lists, inserts,
replacement cancellations, and reminder receipts to this namespace. It does
not fall back to TheTochka when the Veil tables are missing. Startup validates
the additional schema once that tenant's bookings are enabled; disabled tenants
do not require their tables to exist yet. Normal startup does not create them,
and the existing `--migrate` command still handles the legacy shared schema.
No new database URL or token is needed. Keep the existing credentials private.

After executing both scripts, this query should return 25 hall tables and 2 VIP entries:

```sql
SELECT id, type, capacity, status, created_at, updated_at
FROM secret_veil_tables
ORDER BY id;
```

Creating the tables alone does not open bookings. `BookingEnabled` stays false
until capacities, weekday-aware booking rules, and the frontend API integration
are complete. The page continues using its local floor-plan preview meanwhile.

## Current indoor floor plan

The annotated seating chart supersedes the older hall illustration and SVG
numbering. Hall tables 12-23 are small and round; tables 1-11 and 24-25 are
larger and rectangular. Map markers, the shape legend, selection details, and
accessible labels distinguish these types. The selection details show the
confirmed maximum of 4 guests for small round tables and 6 for larger tables.

The entrance vestibule is at the lower left. The shaded room at the upper
right is the staircase to the VIP rooms on the second floor, not two VIP rooms
in the main hall. Both spaces use hatching on the map. The bar and hookah area
are on the right; restrooms (М/Ж) are at the upper left. This orientation matches
the latest annotation and the clockwise rotation of the earlier seating chart.

The hall is rendered as a schematic from the same coordinates as its table
markers, with shaded tabletops, continuous upholstery bands, wall thickness,
door swings, stair treads, and facility counters. Relative positions follow the
sketch, with wider aisles for legibility. Furniture is illustrative, not measured
dimensions or a seat inventory. Landmarks are labels, not selectable inventory.
The drawing expands the room to 1720 by 2140 schematic units while keeping
tabletop and upholstery dimensions fixed. Table centers spread across the new
floor area; perimeter facilities stay anchored to their respective walls. The
picker provides a larger viewport and minimum hall width so the extra aisle
space remains visible on desktop and mobile.
The visible tabletops are slightly enlarged within that extra floor space.
Every hall table has a minimum 44 by 44 CSS-pixel selection target, with clear
gaps between neighboring targets. Number labels retain a 13-pixel minimum.
The architectural palette matches the retained VIP illustration. Table numbers
sit on the drawing; amber highlights appear on hover or selection. Selection
targets retain clear gaps at the fitted scale, including on narrow screens.
The vector artwork also includes upholstery arms and stitching, subtle wood
grain and edge reflections, small tabletop accessories, floor-board lines,
bar cabinetry and a sink, a hookah outline, washroom fixtures, and entrance
threshold details. These are illustrative drawing details, not confirmed venue
equipment. The stair caption sits below the treads so it does not obscure them.
Hall numbers and annotations grow with zoom while retaining readable minimum
sizes in the fitted view.

The balcony and terrace are separate spaces but are out of season and omitted
from the current picker. They must not contribute selectable tables or counts.
The VIP illustration and its existing UI table IDs 28 and 29 are retained;
whether each VIP room is booked as a whole still needs confirmation.

## Table picker preview

With bookings disabled, the existing reservation section opens The Veil's own
table picker instead of the hardcoded TheTochka map. It includes the hall and
VIP plans, a table-number grid, zoom controls, and local
selection confirmation. The BRON header and shared page sections remain intact.

The preview currently has 27 selectable entries: 25 hall tables and 2 VIP
entries. Hall capacities are confirmed at 4 or 6 guests; the smaller VIP room
28 accommodates up to 6, and the larger room 29 up to 8. Availability is
unknown; no table/slot/reservation API is called by the
preview. Selection is
kept only in the current page, and does not create a reservation. Its internal
IDs include the area (for example `hall-1` and `vip-28`); these still need
mapping to isolated Turso inventory before bookings can be enabled.

The retained VIP drawing is in `my-site/public/tenants/secret-veil/floor-plans/`.
Marker positions, landmark labels, and provisional table metadata are in
`my-site/src/experiences/veil-floor-plan-data.ts`; the hall artwork is generated
by `veil-floor-plan-diagram.ts`. Confirm VIP booking rules before
connecting real bookings.
