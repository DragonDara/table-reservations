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
hours, and storage setup are pending. Booking endpoints return 503 and reminder
processing skips the venue. Public branding and the rating endpoint remain
available. Do not enable bookings until its Turso data is isolated from the
existing restaurant; the current database maps one tenant per business type.

The 2GIS score and review count are a saved snapshot. `Rating.Mode = Snapshot`
does not start a paid scraping job or require an Apify token.

## Floor plan assumptions

Treat the balcony and terrace as separate booking areas pending the venue's
confirmation. Their reference illustrations currently show balcony tables 1-9
and terrace tables 1-13. Repeated display numbers across areas are valid;
reservation inventory must use distinct internal table IDs and identify the
area alongside each table number.

The terrace, hall, and VIP SVG overlays use table IDs 1-13, 14-27, and 28-29
respectively. These artwork IDs do not include a separate balcony inventory and
must not be treated as the complete venue inventory. Hall numbering also differs
between the full plan and individual detail images, so final numbering and seat
capacities still need confirmation before booking inventory is configured.
