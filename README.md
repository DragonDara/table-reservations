# Table Reservations

A restaurant table-reservation website for TheTochka. The repository contains a Vite frontend and an ASP.NET Core API. The frontend provides the booking form and interactive floor plan; the backend stores reservations in Google Sheets and sends WhatsApp notifications.

## Repository layout

```text
.
├── my-site/                 Frontend (HTML, CSS, TypeScript, Vite)
│   ├── index.html           Page markup and floor-plan elements
│   ├── src/main.ts          UI behavior and reservation flow
│   ├── src/style.css        Site styling and responsive layout
│   ├── src/api.ts           Handwritten API client
│   ├── src/backlog/         Generated API client (currently unused)
│   └── public/              Static assets
├── table-reservations/      ASP.NET Core backend
│   ├── Controllers/         Tables, reservations, and rating endpoints
│   ├── Services/            Google Sheets and WhatsApp integrations
│   ├── Models/              API/domain models
│   └── Program.cs           Application configuration and middleware
└── table-reservations.slnx  .NET solution
```

## Frontend development

Requirements:

- Node.js version supported by Vite 8
- npm

Install dependencies and start the development server:

```powershell
cd my-site
npm install
npm run dev -- --host 127.0.0.1 --port 5173
```

Open <http://localhost:5173>.

Type-check and create a production build:

```powershell
npm exec tsc -- --noEmit
npm run build
```

The production output is written to `my-site/dist`.

### API URL

The frontend reads its API origin from `VITE_API_BASE_URL`. If the variable is absent, requests use the same-origin `/api` path:

```text
VITE_API_BASE_URL=https://your-backend.example.com/api
```

Vite currently has no development proxy. Running only the frontend on port 5173 therefore renders the interface, but `/api` requests will not reach a separately running backend unless `VITE_API_BASE_URL` is configured.

The active frontend uses `src/api.ts`. The generated client in `src/backlog` is not currently imported by the application.

## Backend overview

The backend targets .NET 10 and exposes these implemented endpoints:

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/api/Tables?scheduledAt=...` | Return tables and availability |
| `GET` | `/api/Tables/{tableId}/availability?scheduledAt=...` | Check one table |
| `POST` | `/api/Reservations` | Validate and create a reservation |
| `GET` | `/api/Rating` | Return the cached 2GIS rating |

To run it independently:

```powershell
dotnet run --project table-reservations/table-reservations.csproj
```

The backend enforces restaurant hours of 12:00–04:00, requires reservations to be at least five minutes in the future, detects overlapping bookings, and prevents duplicate bookings for the same phone number. Reservation times are interpreted in the `Asia/Almaty` time zone.

Google Sheets is used as the data store. Successful reservations trigger WhatsApp notifications, and a background service checks every five minutes for reminders that should be sent one hour before a booking.

### Required backend configuration

Provide secrets through environment variables, user secrets, or deployment configuration. Do not commit them.

| Configuration key | Purpose |
| --- | --- |
| `GoogleSheets:SpreadsheetId` | Target spreadsheet |
| `GoogleSheets:CredentialsJson` or `GoogleSheets:CredentialsJsonPath` | Google service-account credentials |
| `GreenApi:ApiUrl` | Green API base URL |
| `GreenApi:IdInstance` | Green API instance ID |
| `GreenApi:ApiTokenInstance` | Green API token |
| `GreenApi:AdminPhone` | Administrator notification number |
| `Apify:Token` | Token used to retrieve the 2GIS rating |

For environment variables, .NET configuration uses double underscores, for example `GoogleSheets__SpreadsheetId`.

## Current limitations

- The frontend defines helpers for `/api/health` and `GET /api/Reservations/{id}`, but the backend does not implement those routes.
- The ASP.NET app serves static files from `wwwroot`, while Vite builds to `my-site/dist`; no repository automation currently connects those directories for a combined deployment.
- There are no automated tests, lint script, or CI workflow.
- Much of the frontend is concentrated in one HTML file, one TypeScript file, and one stylesheet, so changes should be kept focused and reviewed carefully.

## Working conventions

- Frontend-only work belongs under `my-site` unless an API contract genuinely needs to change.
- Do not modify backend code merely to make local frontend development work; configure the frontend API URL instead.
- Preserve unrelated working-tree changes and verify scope with `git status` and `git diff` before committing.
- Keep credentials and local `.env` files out of Git.
