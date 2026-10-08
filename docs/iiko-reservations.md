# Restaurant reservations and iiko

## Activation

The integration runs only for `BusinessType: Restaurant` with `Pos.Enabled: true`.
Car-wash and POS-disabled restaurant bookings retain their existing local flow.
The restaurant must match `Turso:RestaurantOrganizationId`; this change does not
enable sharing the legacy restaurant tables between tenants.

Configure these fields on the existing `Organizations:Items` entry. Keep the client
secret and API key in backend secrets or environment configuration, never in `Frontend` or
any `VITE_` variable. Do not replace the rest of the organization configuration.

```json
{
  "Pos": {
    "Enabled": true,
    "Provider": "iiko",
    "BaseUrl": "https://api-ru.iiko.services/",
    "AppId": "<iiko application UUID>",
    "ClientSecret": "<backend application secret>",
    "ApiKey": "<backend iikoWeb API key>",
    "OrganizationId": "<iiko organization UUID>",
    "TerminalGroupId": "<iiko terminal group UUID>",
    "TableMappings": {
      "1": "<iiko table UUID>",
      "2": "<iiko table UUID>"
    }
  }
}
```

Authentication uses `POST /api/v2/access_token`; the deprecated
`/api/1/access_token` and its `ApiLogin` configuration are no longer used.
Register an application at https://public-api.iikoweb.ru/portal to obtain
`AppId` and the one-time `ClientSecret`. Generate `ApiKey` in iikoWeb under
"Integrations" -> "API Keys". The request sends `appId`, `clientSecret` and
`apiKey`; subsequent requests use the returned JWT as a Bearer token. Tokens
last one hour, and the client renews after 50 minutes without a refresh token.
Existing deployments must configure all three v2 credentials before enabling POS.

Map every active local table ID to a unique, nonempty iiko table UUID from the
configured terminal group. The example is deliberately incomplete. Table numbers
and names are not identities and are not used to guess mappings. Configure
booking hours for the venue's Kazakhstan time.

Before enabling production traffic:

1. Back up the database and audit existing future local bookings against iiko.
   Existing bookings are not automatically imported or linked by phone/name.
2. Run the additive migration with the intended environment and its backend secrets:

   ```powershell
   dotnet run --project table-reservations --no-launch-profile -- --environment Development --migrate
   ```

   This adds `restaurant_booking_operations`, `restaurant_booking_rows`, indexes,
   and nullable `table_reservations.ends_at`. Existing rows are retained. Normal
   startup validates the schema rather than running DDL.
3. Verify API access and terminal connectivity in a nonproduction terminal group.
   In particular, enable the reservations and order-loading permissions below.
4. Test create, pending confirmation, staff edits/cancellation, order opening and
   closing, terminal outage, and an interrupted create response before activation.
   Check the resulting booking in iikoFront, not just the HTTP response.

No live migration or real venue create/cancel was performed during implementation.

## Provider contract

Payloads were checked against the official
[iiko Cloud OpenAPI document](https://api-ru.iiko.services/api-docs/docs).
Relevant endpoints are all POST:

| Endpoint | Purpose |
| --- | --- |
| `/api/v2/access_token` | Backend application authentication (JWT) |
| `/api/1/terminal_groups/is_alive` | Check the configured front-office terminal |
| `/api/1/reserve/available_restaurant_sections` | Inventory for `terminalGroupIds` |
| `/api/1/order/init_by_table` | Request loading of POS-created orders; await its HTTP response |
| `/api/1/order/by_table` | Read nested `order` records with `New` or `Bill` status |
| `/api/1/reserve/restaurant_sections_workload` | Reservations overlapping the booking horizon |
| `/api/1/reserve/create` | Submit one reservation for all selected tables |
| `/api/1/reserve/status_by_id` | Resolve the exact reservation UUID and creation state |
| `/api/1/reserve/cancel` | Request cancellation of a linked previous reservation |

`order/init_by_table` requires iiko 7.7.1+ and the **Orders: loading data**
restriction group. Cancellation requires 8.2.6+ and supports reservations in
`New` state. Verify the deployed version, permissions and quotas with iiko.

Order reads await a successful `order/init_by_table` HTTP response with a
nonempty `correlationId`, then call `order/by_table`. Initialization is not
documented as supporting `commands/status`; its correlation ID is not polled.
This avoids the observed `410 Gone` from that unsupported status query without
discarding open-order occupancy. HTTP failures and malformed responses still
block availability; no delay, retry fallback, or empty-order substitution is used.

The initialization response is not treated as proof of complete Cloud propagation.
The checked OpenAPI does not specify an initialization-completion or freshness
guarantee for the subsequent order read. Validate the sequence against iikoFront
with real open/closed orders before production activation, and confirm those
guarantees with iiko. `CheckedAt` records when the local check started, not a
provider synchronization watermark. An empty order response alone cannot prove
there are no unsynchronized POS orders.

Create includes a durable caller-supplied `id`, configured `terminalGroupId`,
`customer.type: regular`, normalized phone, mapped table UUIDs, and a 180-minute
duration. The reservation API uses the regular customer contract; ensure the
venue's customer-data/loyalty consent process meets iiko's requirements. Guest
count retains the existing calculation from selected table capacities.
Times use terminal-local `yyyy-MM-dd HH:mm:ss.fff`. A successful create response
with `creationStatus: InProgress` is accepted locally without waiting for
waiter/front-office approval. Explicit `creationStatus: Error` still rejects the
booking. A later iiko status does not revoke local confirmation; use the local
booking record as authoritative.

Workload bounds apply to reservation start times. The snapshot therefore queries
90 days back (the documented record-retention window) through nine days ahead,
including carry-over reservations and the seven-day online booking horizon.
Validate response size and quotas on the actual venue before rollout.

## Availability

The unavailable interval is the union of active local bookings, unresolved local
holds, iiko reservations, and open orders. An open order blocks its table from now
until the business day's closing time, including an overnight closing time.
A fresh check showing the order closed removes only that order's block.
Later business days are not blocked solely by today's open order.

All table, single-table and slot checks use this merger. Submitting a new booking
requires uncached POS checks before creating a local hold and immediately before
dispatch. Missing mappings, offline terminals, failed initialization, incomplete
responses and expired snapshots fail closed. A failed check does not mean empty
occupancy and does not allow an offline booking to be queued.

The browser fetches slots or table statuses once when the customer enters that
step, and exposes explicit refresh buttons. It does not poll automatically.
Each explicit availability request fetches a new POS snapshot; calls are serialized
per tenant. Availability data expires in the browser after at most 30 seconds;
expired choices are disabled until refreshed.
Busy tables show "Не свободен"; unknown tables cannot be selected. A newly
unavailable selection is cleared while guest details remain.

A staff order or reservation can be entered after the last availability check.
This implementation does not claim a provider-side atomic overlap lock.

## Client contract and recovery

`POST /api/Reservations` requires a UUID `Idempotency-Key` for POS-enabled
restaurants. Retry an interrupted request with the same key and identical payload.
Reusing a key with different normalized data returns `409 IDEMPOTENCY_CONFLICT`.

- `200`, `status: Confirmed`, `success: true`: locally confirmed after a successful
  iiko create response; waiter/front-office confirmation is not required.
- `202`, `status: Processing`, `success: false`: outcome or replacement cancellation
  is unresolved. Use `POST /api/Reservations/{reservationId}/check` to perform one
  explicit provider check; the client does not poll automatically.
- `409`: conflict or definite rejection; an existing-booking conflict retains the
  overwrite prompt. `BOOKING_PROCESSING` means an existing operation is unresolved.
- `503 POS_UNAVAILABLE`: verification unavailable. A transport/503 error can also
  obscure a persisted operation, so retain the retry key.

The read-only status endpoint and explicit check action are tenant-scoped and
non-cacheable. The browser stores only the retry key, payload hash and known
operation ID in session storage. After reload, a known unresolved ID is shown with
a manual status-check action; no request starts automatically. If a response was
lost before the ID was received, retry with the original details and key. After a
reload where those details cannot be reproduced, contact the venue to resolve the
operation; do not clear browser state simply to bypass an unresolved attempt.

The operation UUID is also the iiko reservation UUID. Local pending rows for all
tables are written in one transaction. A successful iiko `InProgress` response
confirms those rows locally without waiter approval. Unknown transport outcomes
retain their hold and are checked only when a user explicitly requests it; checks
query the exact UUID and never blindly re-send a create. An orphaned hold that was
never dispatched is rejected if explicitly checked, not dispatched later. An
unknown external outcome retains its hold, even after the requested time passes.

Replacement sends the new create before requesting cancellation of linked
originals. An `InProgress` create is accepted locally without waiter approval, but
the replacement remains processing until cancellation of linked originals is
verified. An overlapping original iiko reservation blocks replacement with
`TABLE_TAKEN`; no cancel-first or unverified in-place modification is attempted.
Cancellation is sent at most once per linked ID because its transport outcome can
be ambiguous.

For prolonged `processing`, `cancelling` or `review` operations:

1. Inspect `id`, `organization_id`, `state`, `correlation_id`, `attempts` and
  `created_at` in `restaurant_booking_operations`.
   Avoid exporting `request_json`, which contains guest details.
2. Compare the exact UUID with iikoFront and `reserve/status_by_id`. `review`
   means confirmed provider details differ from the original requested booking.
3. Resolve the booking and any linked originals with venue staff in iiko. A
   verified cancellation/rejection lets recovery release the corresponding hold.
   Missing/unknown status is not proof of rejection. Do not delete holds or reset
   a dispatched operation to `held` to force a retry. There is no admin resolution
   UI in this change.

No automatic provider-status reconciliation is performed for managed bookings.
Later iiko staff edits or cancellations do not update the local booking; local
records are authoritative for bookings made through this API. Current iiko
reservations and open orders still participate in each explicit availability
snapshot.

Confirmation notifications are attempted only after confirmation and claimed once
per operation. This prevents duplicate attempts during replay, but it is not an
exactly-once delivery outbox: a crash or failed send after the claim can lose a
notification. The scheduled reminder worker has been removed, so automatic
pre-reservation reminders are disabled. Monitor delivery failures and treat the
local operation state, not message delivery, as authoritative.

## Verification

Run from the repository root:

```powershell
dotnet test table-reservations.Tests
npm --prefix my-site test
npm --prefix my-site run build:static
node my-site/tests/smoke.mjs
```

Provider tests use sanitized mock HTTP responses; repository tests use in-memory
SQLite. These do not certify the venue's permissions, Cloud freshness guarantees,
quotas, customer-data consent process, or behavior under real multi-instance load.