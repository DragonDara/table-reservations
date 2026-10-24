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

`Organizations.Items[2].BookingEnabled` is true. The page opens the normal
reservation flow with live availability from the isolated Turso inventory.
Restart the API after changing tenant settings because its organization registry
is constructed at startup, then reload the page to fetch the updated public config.
`NotificationsEnabled` is true: configured Telegram and WhatsApp integrations
can deliver booking confirmations. Reminder preference remains hidden in the UI.

The 2GIS score and review count are a saved snapshot. `Rating.Mode = Snapshot`
does not start a paid scraping job or require an Apify token.

## Notification setup

The existing BRON Telegram bot can serve The Veil as another tenant. No separate
bot or webhook is needed. The current `/connect` flow supports staff groups and
supergroups; broadcast channels do not support this flow. Keep the shared bot's
existing production webhook: starting a local server must not register a new
webhook for that same bot.

The Veil is currently `Organizations.Items[2]`. Confirm that index still contains
`Id = secret-veil` before applying environment settings. Configure the following
on the backend in DigitalOcean, or under that tenant in ignored local settings:

```text
Organizations__Items__2__Telegram__ConnectionCode=<unique private connection code>
Organizations__Items__2__WhatsApp__ApiUrl=<Green API apiUrl>
Organizations__Items__2__WhatsApp__IdInstance=<Green API idInstance>
Organizations__Items__2__WhatsApp__ApiTokenInstance=<Green API apiTokenInstance>
Organizations__Items__2__WhatsApp__AdminPhone=<confirmed admin recipient>
```

Use the chosen sender's Green API instance. An existing BRON sender can be used
by supplying its credentials explicitly for The Veil; another tenant's settings
are not inherited. A separate venue sender needs its own authorized instance.
The public `wa.me` contact link is not a notification connection or sender.
Store the API token and connection code as backend secrets, never as frontend
`VITE_` variables or in tracked settings. The development example includes a
third tenant entry; merge it into an existing private file instead of replacing
the other tenants' credentials.

For a staff group, add `@reservremindbot`, make it an administrator, and send
`/connect@reservremindbot <connection code>` from a group administrator's personal
account after restarting/deploying the backend with that code. One group belongs
to one tenant. Connecting another group for The Veil moves its subscription.
See [Telegram setup](telegram-notifications.md) for the full connection flow.

`NotificationsEnabled` is already true in the tracked tenant settings; no
DigitalOcean override is required unless the environment sets it to false.
Restart the backend after configuring destinations and credentials. This enables
booking confirmations for configured integrations; it does not enable
hourly reminders in the UI. `Frontend.Features.ShowReminderOption` remains false,
so keep that unchanged unless reminders are also requested. Check delivery using
an agreed test recipient and booking: the API returns `whatsAppSent`,
`adminWhatsAppSent`, and `telegramSent`. A delivery failure does not cancel the
saved booking.

## Opening hours

`Organizations.Items[2].OpeningHours` in `appsettings.json` stores the confirmed
weekly schedule in local Atyrau time: Monday–Thursday and Sunday 12:00–02:00,
Friday–Saturday 12:00–03:00. Each entry belongs to the day the venue opens;
closing is on the following calendar day. These hours configure booking
validation; The Veil page does not display a work-schedule block.

Opening hours are separate from `BookingTime`, which controls reservation start
slots. Server validation resolves the weekly opening/closing times for the
selected opening day. Missing weekdays in an explicit weekly schedule are closed.

`ReservationDurationMinutes = 60` applies to both hall tables and VIP rooms.
Starts are hourly from 12:00 through **00:00 inclusive**;
`BookingTime.ReservationDeadline = "00:00"` and
`ReservationDeadlineInclusive = true` make midnight the last start. Midnight
belongs to the following calendar day of the selected evening; that visit ends
at 01:00. Each complete visit must fit before the venue closes. The existing
seven-opening-day horizon and five-minute lead time still apply.

New Veil bookings store their UTC `ends_at`, so later duration changes do not
alter existing reservations. The existing restaurant retains its three-hour
default and existing booking behavior. VIP 28/29 each represent an entire room:
one booking makes that room unavailable for the complete one-hour interval,
independently of the room's maximum guest count.

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
larger VIP room 29 for up to 8, both with type `VIP`. Each is booked as a whole
room. Select all INSERT statements together in the Turso
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

The configured `BookingEnabled = true` opens the reservation flow once the
updated API is running. If explicitly disabled later, the page falls back to
local floor-plan preview.

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
The VIP illustration and its existing UI table IDs 28 and 29 are retained.
Each VIP entry books the entire room.

## Table picker and preview

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
IDs include the area (for example `hall-1` and `vip-28`). When bookings are
enabled, selections map to numeric Turso IDs 1–25 and 28/29. The shared wizard
loads available starts from `/api/Tables/slots`, then table status/capacity
from `/api/Tables?scheduledAt=...`. Opening the picker refreshes availability;
missing or occupied inventory cannot be selected. The server checks conflicts
again inside the booking transaction. A room taken during submission returns
the guest to table selection without discarding their contact details.
Existing-reservation replacement continues using the shared overwrite dialog.
The chosen numeric ID, actual calendar timestamp, and hall/whole-room section
are submitted through the normal reservation API.

The retained VIP drawing is in `my-site/public/tenants/secret-veil/floor-plans/`.
Marker positions, landmark labels, and provisional table metadata are in
`my-site/src/experiences/veil-floor-plan-data.ts`; the hall artwork is generated
by `veil-floor-plan-diagram.ts`. The artwork and shared page structure are
preserved when live bookings are enabled.
