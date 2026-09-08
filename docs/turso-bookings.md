# Turso bookings

## Setup

Keep credentials on the backend only: ignored
`table-reservations/appsettings.Development.json`, .NET user secrets, or
`Turso__DatabaseUrl` / `Turso__AuthToken` environment variables.
Never use a `VITE_` variable for a database token.
The production client uses .NET HttpClient and Turso's
[SQL-over-HTTP protocol](https://docs.turso.tech/sdk/http/reference); no database dependency is needed in the browser.

The existing schema has no organization_id. These configured IDs bind each
business table to exactly one tenant:

- `Turso:RestaurantOrganizationId = thetochka`: `tables`, `table_reservations`.
- `Turso:CarWashOrganizationId = thetochka-carwasher`: `carwash_boxes`, `box_reservations`, and the carwash catalog.

An additional tenant is rejected until its storage is configured; it cannot
silently reuse another organization's reservations.
Local frontend routes: `/lounge`, `/carwash` (also the organization-ID paths).
Production uses tenant subdomains and same-origin `/api`; the production
middleware intentionally ignores tenant headers on shared hosts.

## Additive migration

The catalog and both reservation tables must already exist.
Run once against each configured environment before starting the updated API:

```powershell
dotnet run --project table-reservations --no-launch-profile -- --environment Development --migrate
```

Use the appropriate environment for a deployment, with its secrets configured.
The command exits without starting the web server or reminder worker. It adds:

- `box_reservations.services_json`: non-null TEXT containing a JSON array,
  with an array-validity constraint and default `[]`.
- `reservation_reminders`: lounge reminder delivery receipts.
- Availability and phone lookup indexes.

Existing junction-table service IDs are backfilled only for reservations with
an empty array. Existing tables, bookings, and junction rows are retained.
New carwash bookings use `services_json` as their authoritative service list;
the legacy `box_reservation_services` table is not updated for new bookings.
Repeating the migration is safe. Normal startup only validates schema; it does
not create or modify tables.

Example stored array:

```json
["body_wash", "interior_polish", "tire_blackening"]
```

## Prices and duration

The current catalog's `price_minor` values are whole KZT (e.g. 2500), so
`Turso:CatalogPricesAreKzt` defaults to true. If catalog prices are converted
to tiyn, set it to false at the same time. Reservation `total_minor` is always
stored in tiyn: a 4000 KZT booking stores 400000. API prices are whole KZT.
The server calculates totals; request-supplied prices/labels are not trusted.
No automatic seasonal surcharge is currently applied.

Session duration rules are in `Services/BookingRules.cs`:

- `complex_wash`: 90 minutes.
- `body_wash + interior_polish + tire_blackening`: 60 minutes.
- `body_wash + interior_polish`: 50-minute reserved window (conservative
  allowance for the stated service taking under 50 minutes).

Catalog assumption: the existing `interior_polish` ("Полировка салона
(панель)") represents the interior work/polishing; there is no separate
interior-cleaning item in the current catalog. Adjust the IDs/rules if these
are meant to be distinct services.

Other selections sum `carwash_service_prices.duration_minutes` when every
selected service has a duration. Otherwise they use the configurable
`Turso:DefaultCarWashMinutes` (60 by default, pending business confirmation).
Set it to null in JSON to reject unconfigured selections.
For a full package plus extras, the package always retains its 90 minutes;
extra durations (or the default allowance for extras) are added.
Package inclusions come from `carwash_package_items`; included services are
removed from the separate selection to avoid double charging.

## Availability and safety

`GET /api/carwash/catalog` returns categories, services/prices and package items.
`POST /api/carwash/quote` accepts `{vehicleCategoryId, serviceIds: []}`.
`POST /api/carwash/availability` accepts the same selection plus `date`
(`yyyy-MM-dd`) and returns `{quote, slots}`.
`POST /api/Reservations` receives the category and service-ID array alongside
the plate, customer details and selected local `scheduledAt`.

`/reservations` displays the current tenant's busy tables or wash boxes with a
date picker (today by default) and refresh button. Locally, open
`http://localhost:5173/reservations?org=thetochka` for the lounge or use
`org=thetochka-carwasher` for the carwash. On a tenant domain, open `/reservations`.
The selected `date=yyyy-MM-dd` is retained in the URL for sharing/reloading.

`GET /api/Reservations?date=yyyy-MM-dd` reads occupancy directly from Turso.
It returns pending, confirmed and in-progress reservations overlapping that
Kazakhstan calendar day, including bookings carried over from the previous
night, sorted by start time. Cancelled, completed and no-show records are
excluded. Past dates can be selected; this is an occupancy view, not a history
of completed visits. The employee list contains start/end times, table or box IDs,
customer names and phone numbers, and carwash service labels. Plates are not returned.
The list response disables HTTP caching. The application currently has no employee
authentication on this endpoint; hiding links does not restrict access to the URL.

Carwash availability uses active boxes and the entire requested session,
not just its start time. Adjacent sessions can share a box; overlapping ones
cannot. Confirmed, pending and in-progress bookings occupy capacity.
The server assigns the box inside a BEGIN IMMEDIATE transaction and rechecks
capacity before inserting. An overwrite cancels the old booking and inserts
the replacement in that same transaction; failures roll back both operations.
Phone matching alone is not proof of identity: the inherited overwrite flow
should gain OTP verification before being used where abuse is a concern.

Slots are restricted to the tenant schedule, the next seven booking days,
and at least five minutes lead time. A carwash session must end by closing.
The frontend never fabricates availability when a request fails.
Lounge bookings remain three-hour reservations using lounge tables only.

Booking timestamps are canonical UTC TEXT (`yyyy-MM-dd HH:mm:ss`) in the DB.
API wall-clock times are Kazakhstan UTC+05:00. The fixed booking zone avoids
stale Windows time-zone data; this is intended for current/future bookings,
not historical pre-March-2024 dates. Existing non-UTC reservation data must be
converted before use (the inspected database had no bookings at migration time).

Disable `ReservationReminders:Enabled` for local checks; enable it when
WhatsApp is configured. The reminder worker currently supports lounge bookings
only. Run one reminder worker instance to avoid duplicate sends across replicas.
Booking notifications run after commit; notification failure does not undo a booking.

## Verification

```powershell
dotnet test
npm test --prefix my-site
npm run build --prefix my-site
```

From `my-site`, `node tests/smoke.mjs` checks built route/asset delivery.
Repository tests execute real SQL against isolated in-memory SQLite; they
never load Turso credentials or send notifications. HTTP protocol tests cover
parameter encoding, transaction batons, commit, rollback and connection loss.

To check the configured live catalog and availability without writing bookings
or starting notification workers:

```powershell
dotnet run --project table-reservations --no-launch-profile -- --environment Development --check-db
```
