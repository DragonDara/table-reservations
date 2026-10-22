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

## Current indoor floor plan

The annotated seating chart supersedes the older hall illustration and SVG
numbering. Hall tables 12-23 are small and round; tables 1-11 and 24-25 are
larger and rectangular. Map markers, the shape legend, selection details, and
accessible labels distinguish these types without assigning seat capacities.

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
entries. These are provisional UI entries,
not confirmed bookable inventory. All capacities are null and availability is
unknown; no table/slot/reservation API is called by the preview. Selection is
kept only in the current page, and does not create a reservation. Its internal
IDs include the area (for example `hall-1` and `vip-28`); these still need
mapping to isolated Turso inventory before bookings can be enabled.

The retained VIP drawing is in `my-site/public/tenants/secret-veil/floor-plans/`.
Marker positions, landmark labels, and provisional table metadata are in
`my-site/src/experiences/veil-floor-plan-data.ts`; the hall artwork is generated
by `veil-floor-plan-diagram.ts`. Confirm capacities and VIP booking rules before
connecting real bookings.
