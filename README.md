# Table Reservations

## Work without installing local runtimes

This repository includes a GitHub Codespaces configuration with .NET 10 and
Node.js 24. To work entirely in a browser:

1. Open the repository on GitHub.
2. Select **Code**, **Codespaces**, then **Create codespace on master**.
3. Create a feature branch in the Codespaces terminal:

   ```bash
   git switch -c feature/<short-name>
   ```

Dependencies are restored automatically when the Codespace is created.

### Run the application

Start the API:

```bash
dotnet run --project table-reservations/table-reservations.csproj --launch-profile http
```

In a second terminal, start the frontend:

```bash
npm run dev --prefix my-site -- --host 0.0.0.0
```

Use the Codespaces **Ports** panel to open the Vite frontend on port `5173` or
the ASP.NET Core API on port `5183`.

### Configuration and secrets

Never commit service credentials. Each integration is configured beneath its
organization entry; there are no shared Google Sheets, WhatsApp, rating, or POS
credentials. Use the ignored development settings file described below or
equivalent `Organizations__Items__<index>__...` Codespaces secrets.

When finished, push the feature branch, stop or delete the Codespace, sign out
of GitHub on shared machines, and close the private browsing window.

## Tenant configuration

Tenant routing resolves subdomains of the base domains listed under
`TenantRouting:BaseDomains` (and `*.localhost` during local development).
On shared hosts, the frontend sends an explicit tenant selection through
`X-Organization-Id`. A recognized tenant hostname always takes precedence.

### Tenant pages and URLs

The same frontend serves both organizations with separate booking pages:

| Organization | Local URL | Shared-host path | Tenant hostname |
| --- | --- | --- | --- |
| Lounge | `http://localhost:5173/lounge` | `/lounge` or `/thetochka` | `thetochka.bron.cafe` |
| Carwash | `http://localhost:5173/carwash` | `/carwash` or `/thetochka-carwasher` | `thetochka-carwasher.bron.cafe` |

Trailing slashes are supported. Explicit paths take precedence over query
parameters, browser storage, and `VITE_ORGANIZATION_ID`. Existing `/?org=thetochka`
and `/?org=thetochka-carwasher` links still work. On tenant hostnames, open `/`;
the API resolves the organization from the host. The carwash page uses the
configured name, theme, services (`Frontend:BusinessUi:Services`, pipe-separated),
contact links, and booking hours. Its time choices come from the tenant schedule;
reservation conflicts are checked by the API when submitting.

For deployment, serve `my-site/dist` with an SPA fallback to `/index.html` for
frontend paths, and forward `/api/*` to the backend while preserving the original
host. Build assets use root-relative URLs so direct links and refreshes work on
nested paths. Custom tenant hostnames require DNS and hosting configuration;
adding a page does not provision a domain.

Copy `table-reservations/appsettings.Development.example.json` to
`table-reservations/appsettings.Development.json` and fill in each tenant's
Google Sheets, WhatsApp, rating, and POS secrets. The development file and
service-account JSON files are git-ignored. Do not place secrets in the tracked
`appsettings.json`.

ASP.NET environment variables can be used instead of the ignored JSON file. For
example, the first organization's spreadsheet id is
`Organizations__Items__0__SpreadsheetId`; nested settings follow the same
double-underscore convention.

Each organization also defines its own booking window and slot interval:

```json
"BookingTime": {
  "StartTime": "08:00",
  "EndTime": "20:00",
  "ReservationDeadline": "18:00",
  "SlotDurationMinutes": 60
}
```

Times use the 24-hour `HH:mm` format. `EndTime` is the closing time displayed to
customers. `ReservationDeadline` is the exclusive last-slot boundary and must
fall inside the start/end window; when omitted, it defaults to `EndTime`. A time
earlier than `StartTime` represents an overnight window (for example,
`12:00`–`04:00`). The backend publishes slots generated up to the deadline and
rejects reservations outside that exact set.
