# Add Multitenancy (Per-Organization) + Pluggable Business Types

> Implementation plan for adding multitenancy to the Table Reservations API.
> Tenants are distinguished by organization, each backed by its own Google Sheets
> spreadsheet, and each organization declares a **business type** (restaurant or
> car wash) that plugs in different rules, validation, and sheet schema.

## Context & Findings

The app is currently a single-tenant reservation API backed by Google Sheets.

- `Program.cs` registers `GoogleSheetsService` as scoped and wires controllers,
  WhatsApp, the reminder hosted service, and a POS factory. CORS already
  whitelists per-tenant subdomains (`theveil.bron.cafe`, `thetochka.bron.cafe`),
  confirming tenant-by-subdomain intent.
- `Services/GoogleSheetsService.cs` resolves a **single** spreadsheet through
  `GetSpreadsheetId()` → `_config["GoogleSheets:SpreadsheetId"]` and credentials
  through `CreateCredential()` → `_config["GoogleSheets:CredentialsJson" | "CredentialsJsonPath"]`.
  Every public method calls `CreateService()` + `GetSpreadsheetId()`, so tenant
  scoping must flow into both. Sheet names/ranges are hardcoded
  (`Столики!A2:C100`, `Брони!A2:H10000`, `Брони!A:H`), as is
  `ReservationDuration.Hours` and `TableType` (VIP/Обычный).
- `Controllers/ReservationsController.cs` injects `IGoogleSheetsService` +
  `IWhatsAppNotificationService` and hardcodes restaurant rules: min +5 min,
  working hours 12:00–04:00, table-id parsing, VIP labels. This domain logic must
  become a **pluggable per-business-type strategy**.
- Existing `Pos.PosAdapterFactory` / `IPosAdapter` is the pattern to mirror for
  provider/strategy resolution.

## Decisions

- **Tenant identity:** resolve from **subdomain first**, fall back to the
  **`X-Organization-Id` header**.
- **Config store:** new `Organizations` section in `appsettings.json`.
- **Credentials:** **per-organization** (each org supplies its own spreadsheet id
  + Google credentials).
- **Business type:** **full pluggable strategy** — restaurant vs car wash have
  different rules/validation and different sheet schemas.
  - Restaurant sheet: id, tableType (VIP/Обычный), seats + reservations
	(id, tableIds, name, phone, start, status, remind, sent).
  - Car wash sheet: **id, plate number, reservation time, phone number, wash
	service type**.
- **Scope:** full implementation + DI wiring.
- **Persistence of this plan:** saved into the repo as `docs/multitenancy-plan.md`
  so it lives with the source code.

## Design Overview

1. **Tenant resolution pipeline**
   - `TenantContext` (scoped) holding resolved `OrganizationId`, `BusinessType`,
	 and the org's Google Sheets options (spreadsheet id + credentials +
	 sheet/range config).
   - `ITenantResolver` + middleware `TenantResolutionMiddleware`: read subdomain
	 from `Host`, else `X-Organization-Id` header; look up in
	 `OrganizationRegistry`; populate `TenantContext`; return `400/404` if
	 unresolved for API routes.
2. **Config model**
   - `OrganizationsOptions` bound from the `Organizations` section: list of orgs
	 keyed by id, each with `Subdomains[]`, `BusinessType`, `SpreadsheetId`,
	 `CredentialsJson`/`CredentialsJsonPath`, and per-type sheet/range settings.
   - `OrganizationRegistry` (singleton) indexes orgs by id and by subdomain.
3. **Make `GoogleSheetsService` tenant-aware**
   - Replace `IConfiguration` reliance with `TenantContext`: `GetSpreadsheetId()`
	 and `CreateCredential()` read from the resolved org, not global config.
	 Sheet names/ranges come from the org's business-type schema config instead of
	 consts.
4. **Pluggable business-type strategy**
   - `IBusinessTypeStrategy` with `BusinessType Type`, request validation
	 (min time, working hours, duration), field/label mapping, and sheet row
	 mapping (read/write) for that type.
   - Implementations: `RestaurantStrategy` (extract current
	 `ReservationsController` rules + existing sheet mapping) and
	 `CarWashStrategy` (plate number + wash service schema).
   - `IBusinessTypeStrategyResolver` selects strategy by
	 `TenantContext.BusinessType`.
5. **Refactor controller & reminder service**
   - `ReservationsController.CreateReservation` delegates validation +
	 persistence to the resolved strategy instead of hardcoded restaurant logic.
   - `ReservationReminderService` (hosted) must iterate over **all**
	 organizations rather than a single sheet — create a scope per org and set
	 `TenantContext` before calling sheet methods.
6. **Models**
   - Generalize/extend `ReservationInfo` (or add per-type request DTOs) to carry
	 car-wash fields (plate number, wash service type). Keep restaurant fields
	 working.

## Risks / Open Items

- `ReservationReminderService` currently assumes one sheet; multi-tenant
  iteration changes its loop and needs org enumeration from the registry.
- `TableType`/`ReservationInfo` are restaurant-shaped; car wash needs either
  polymorphic DTOs or a shared superset — the plan uses per-type request DTOs +
  a common base to avoid breaking the restaurant flow.
- Secrets must never be stored in tracked `appsettings.json`. Development uses
  the ignored `appsettings.Development.json`; production uses environment
  variables or a managed secret store such as Key Vault.
- CORS list must include every tenant subdomain; today it's a static list in
  `Program.cs`.

## Steps

1. Create `docs/multitenancy-plan.md` — save this full plan document into the
   repo so it is versioned with the source code.
2. Create `Models/Tenancy/BusinessType.cs` — enum `{ Restaurant, CarWash }`.
3. Create `Configuration/OrganizationsOptions.cs` — options classes:
   `OrganizationsOptions` (list), `OrganizationOptions` (`Id`, `Subdomains[]`,
   `BusinessType`, `SpreadsheetId`, `CredentialsJson`, `CredentialsJsonPath`,
   `Sheets` schema settings).
4. Add `Organizations` section to `appsettings.json` — seed the existing bar
   org(s) plus one example car-wash org with its spreadsheet id, credentials,
   subdomains, and sheet/range config.
5. Create `Services/Tenancy/TenantContext.cs` — scoped class holding
   `OrganizationId`, `BusinessType`, resolved `OrganizationOptions`.
6. Create `Services/Tenancy/OrganizationRegistry.cs` — singleton built from
   `OrganizationsOptions`; lookup by id and by subdomain.
7. Create `Middleware/TenantResolutionMiddleware.cs` — resolve subdomain from
   `Host`, fallback to `X-Organization-Id` header, populate `TenantContext`,
   short-circuit with `400/404` when unresolved on `/api` routes.
8. Refactor `Services/GoogleSheetsService.cs` — inject `TenantContext`; make
   `GetSpreadsheetId()`/`CreateCredential()` read the resolved org; replace
   hardcoded ranges/sheet names with org schema config.
9. Create `Services/BusinessTypes/IBusinessTypeStrategy.cs` — validation, label
   mapping, and sheet read/write row mapping contract.
10. Create `Services/BusinessTypes/RestaurantStrategy.cs` — move current
	`ReservationsController` restaurant rules (min +5 min, 12:00–04:00 hours, VIP
	labels) and existing row mapping here.
11. Create `Services/BusinessTypes/CarWashStrategy.cs` — schema
	id/plate/time/phone/wash-service, with its own validation rules and row
	mapping.
12. Create `Services/BusinessTypes/BusinessTypeStrategyResolver.cs` — resolve
	`IBusinessTypeStrategy` from `TenantContext.BusinessType`.
13. Add per-type request DTOs under `Models` — e.g. base
	`ReservationRequestBase` plus `RestaurantReservationRequest` and
	`CarWashReservationRequest` (plate number, wash service type); keep
	`ReservationInfo` compatible.
14. Refactor `Controllers/ReservationsController.cs` — inject strategy resolver +
	`TenantContext`; delegate validation and append/duplicate checks to the
	resolved strategy.
15. Update `Services/ReservationReminderService.cs` — iterate all organizations
	from `OrganizationRegistry`, create a DI scope per org, set `TenantContext`,
	then run existing reminder logic per tenant.
16. Wire everything in `Program.cs` — `Configure<OrganizationsOptions>`, register
	`OrganizationRegistry` (singleton), `TenantContext` (scoped), strategies +
	resolver, and add `app.UseMiddleware<TenantResolutionMiddleware>()` before
	`MapControllers`.
17. Update CORS in `Program.cs` — drive allowed origins from the organizations'
	subdomains (or extend the static list) so every tenant subdomain is
	permitted.
18. Build the solution and fix compile errors — verify the restaurant flow
	behaves as before and the car-wash strategy resolves correctly.

## Phase 2 — Frontend Multitenancy

### Context & Findings

The Vite frontend under `my-site` is currently a single organization-specific
restaurant page rather than a tenant-neutral application shell.

- `my-site/index.html` contains The Tochka-specific navigation, hero copy,
  menu/contact links, rating labels, reservation form, restaurant floor plan,
  social links, and footer text.
- `my-site/src/style.css` is one global design. It hardcodes the color palette,
  font families, header/hero layout, restaurant map styling, and background
  image URLs.
- `my-site/src/main.ts` assumes a restaurant throughout: it initializes the
  table map, validates restaurant hours, constructs a restaurant reservation
  payload, and renders Russian restaurant-specific messages.
- `my-site/src/api.ts` uses the current origin by default, which already allows
  production tenant resolution by subdomain. It does not send
  `X-Organization-Id`, so local development and a frontend hosted on a shared
  domain need an explicit organization-id fallback.
- Organization configuration in `appsettings.json` includes backend secrets.
  The frontend must never download `OrganizationOptions` directly or receive
  spreadsheet ids, credential JSON, credential paths, or sheet schemas.

### Decisions

- Deploy **one frontend application** for all organizations; do not clone
  `index.html`, CSS, or TypeScript per tenant.
- Continue resolving the organization from **subdomain first**. Support an
  explicit `VITE_ORGANIZATION_ID` or query/local-storage override that is sent
  as `X-Organization-Id` when a tenant subdomain is not available, including on
  a shared production host.
- Add a backend endpoint such as `GET /api/tenant/public-config`. The existing
  `TenantResolutionMiddleware` resolves the organization before this endpoint
  returns a strictly allow-listed public DTO.
- Add public frontend settings beneath each organization, but map them to a
  separate response contract. Never serialize `OrganizationOptions` itself.
- Split customization into two layers:
  1. **Organization branding/content:** logo, colors, fonts, images, text,
	 links, contacts, locale, and optional section visibility.
  2. **Business experience:** restaurant or car-wash layout, reservation form,
	 validation hints, and interactive selector.
- Keep tenant assets in predictable public paths such as
  `my-site/public/tenants/{organizationId}/...`. Configuration stores relative
  URLs, while the frontend provides safe default assets when a file is absent.
- Use CSS custom properties for theme tokens and dynamically load only the
  configured, allow-listed font families. Do not inject arbitrary tenant CSS
  or HTML from configuration.
- Bootstrap tenant configuration before initializing rating, form, floor-plan,
  or car-wash behavior. If configuration cannot be loaded, show a controlled
  unavailable state rather than rendering another organization's branding.

### Public Frontend Configuration

Extend each `OrganizationOptions` entry with a nested `Frontend` section whose
values are safe to expose. A corresponding `PublicTenantConfigResponse` should
contain only fields such as:

- Identity: `organizationId`, `businessType`, `locale`, `displayName`,
  `documentTitle`, and optional public rating-provider metadata.
- Theme: semantic colors (`background`, `surface`, `text`, `muted`, `accent`,
  `border`, `warning`), `fontFamily`, `headingFontFamily`, border radius, and a
  small allow-listed `layoutVariant` value.
- Assets: logo, favicon, hero image, hero background, optional gallery images,
  and business-experience asset references. URLs must be public relative paths
  or validated HTTPS URLs.
- Content: navigation labels, hero eyebrow/title/accent/description, CTA text,
  feature bullets, how-it-works title/steps, form labels/placeholders/hints,
  success/error copy, and footer copyright.
- Links/contact: menu, map, phone, WhatsApp, Instagram, Threads, and other
  allow-listed links. Missing links hide their UI elements.
- Features: booleans such as `showRating`, `showHowItWorks`, `showMenuLink`,
  `showReminderOption`, and `showSocialLinks`.
- Business UI data:
  - Restaurant: floor tabs/scenes or a floor-plan asset/config reference,
	opening-hours display text, table-selection labels, and section names.
  - Car wash: wash-service choices, vehicle plate labels, slot-selection copy,
	and any service-card imagery.

Secrets and storage details remain exclusively in backend configuration. The
public endpoint must not expose `SpreadsheetId`, `CredentialsJson`,
`CredentialsJsonPath`, or `Sheets`.

### Frontend Architecture

1. Replace the organization-specific body in `index.html` with a minimal shell:
   an application root, loading state, unavailable state, and the module script.
2. Add `src/tenancy/types.ts` for the public config contracts and
   `src/tenancy/tenant.ts` for tenant-id fallback resolution and configuration
   loading.
3. Add `src/tenancy/theme.ts` to set `document.title`, favicon, document
   language, CSS variables, font links, and a stable
   `data-organization`/`data-business-type` attribute on the root element.
4. Add reusable shell renderers/components for header, hero, feature steps,
   contact/footer, status messages, and shared form controls. Render tenant text
   as text nodes/element properties, not unsanitized `innerHTML`.
5. Add an `IBusinessExperience`-style frontend contract with
   `RestaurantExperience` and `CarWashExperience` implementations. The
   restaurant module owns table availability, floor-plan selection, hours, and
   restaurant payload mapping; the car-wash module owns service selection,
   vehicle plate input, slot selection, and car-wash payload mapping.
6. Refactor `main.ts` into a small bootstrap sequence that loads config, applies
   branding, renders the shell, resolves the business experience, and then
   initializes tenant-specific interactions.
7. Update `api.ts` so every API request uses the same organization fallback
   header when needed. On production tenant subdomains the header may be
   omitted, allowing the host to remain authoritative.

### Runtime Flow

1. The browser opens `https://{subdomain}.bron.cafe` and loads the shared Vite
   bundle.
2. The frontend calls `GET /api/tenant/public-config` on the same origin.
3. `TenantResolutionMiddleware` resolves the organization from the subdomain
   (or the fallback header on a local/shared host).
4. The endpoint maps `TenantContext.Organization` to the public response DTO.
5. The frontend applies the organization's theme/assets/content and selects the
   experience using `businessType`.
6. All subsequent table, availability, rating, and reservation requests use the
   same origin/header tenant identity, so frontend appearance and backend data
   cannot drift to different organizations.

### Caching, Validation, and Isolation

- Return an `ETag` or short `Cache-Control` policy for public tenant config so
  branding changes propagate without rebuilding the Vite app.
- Validate organization ids, layout variants, colors, asset URLs, links, locale,
  and font names during options startup validation. Reject duplicate subdomains.
- Prefer relative tenant asset URLs. If remote assets are supported, allow only
  HTTPS and trusted hosts; update Content Security Policy accordingly.
- Do not silently fall back to a default organization when host/header tenant
  resolution fails. This prevents cross-tenant branding and data confusion.
- If both subdomain and `X-Organization-Id` are supplied, keep the backend's
  subdomain-first rule and optionally reject a mismatched header.
- Test that public configuration never contains backend-only properties and that
  each organization receives only its own public content.

### Phase 2 Steps

19. Extend `Configuration/OrganizationsOptions.cs` — add strongly typed public
	frontend branding, theme, content, links, features, and business UI options;
	add startup validation for tenant-facing values.
20. Extend each organization in `appsettings.json` — add a `Frontend` section
	for display name, text, theme tokens, fonts, images, links, and feature
	switches for both restaurant and car-wash examples.
21. Create a public tenant-config DTO and mapper — expose only allow-listed
	frontend values and explicitly exclude spreadsheet, credential, and sheet
	schema settings.
22. Create `TenantConfigController` — return the current
	`TenantContext.Organization` as `GET /api/tenant/public-config`, with cache
	headers and unresolved-tenant behavior consistent with the middleware.
23. Add backend tests — verify subdomain/header resolution, mismatched tenant
	identity behavior, per-org config mapping, response isolation, and secret
	exclusion.
24. Move organization assets into
	`my-site/public/tenants/{organizationId}` — provide separate logos, favicons,
	hero/gallery images, and business-specific imagery with documented fallback
	assets.
25. Create frontend tenant config/types/bootstrap modules — resolve the optional
	local organization fallback, fetch public config before app startup, and
	fail safely when no tenant is resolved.
26. Create the runtime theme applicator — set CSS custom properties, font links,
	favicon, title, locale, layout variant, and root tenant attributes from the
	public configuration.
27. Refactor `my-site/index.html` — retain only the neutral app shell and move
	organization text, links, images, footer, and restaurant-only markup into
	typed renderers.
28. Split `my-site/src/main.ts` — keep shared bootstrap/navigation/form behavior
	separate from `RestaurantExperience` and `CarWashExperience` modules.
29. Refactor `my-site/src/style.css` — replace fixed colors/fonts/images with
	semantic theme variables and separate shared, layout-variant, restaurant,
	and car-wash styles.
30. Update `my-site/src/api.ts` — carry the resolved organization header for
	local/shared-host scenarios and add the public tenant-config request while
	preserving same-origin production requests.
31. Add frontend tests — cover config loading failures, theme/content rendering,
	restaurant and car-wash form/payload behavior, conditional sections, and
	tenant header propagation.
32. Build and validate both applications — run the .NET build/tests and the Vite
	TypeScript production build, then verify at least one restaurant subdomain,
	one car-wash subdomain, and local header-based tenant selection end to end.
