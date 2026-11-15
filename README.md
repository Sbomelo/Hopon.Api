# Hopon
 
 Real-time trip notifications and bus tracking for intercity bus passengers in South Africa.
 

## The Problem
 
Intercity bus passengers in South Africa — particularly those travelling overnight on services like Intercape — have no reliable way to know when their bus will arrive at their stop.
 
This leads to real, everyday problems:
 
- Passengers arrive at dark, isolated bus stations hours too early because they don't know the bus's actual ETA.
- Families have no way to know when their loved one will arrive so they can arrange a safe pickup.
- Passengers fall asleep on the bus and miss their stop with no reminder system in place.
- Delays and incidents are not communicated, leaving passengers waiting with no information.

 
## The Solution
 
**Hopon** is a web-based system that gives bus passengers real-time access to their trip status, ETA notifications, and stop reminders — all secured behind their purchased ticket.
 
Product goals (not all implemented; see `docs/HANDOFF.md` section A for verified status):
 
- **OTP-based login** — no account creation needed. Passengers log in using their phone number and a one-time PIN sent when they buy their ticket.
- **Live bus location** — see where the bus is on a map, restricted to your specific trip only.
- **Smart notifications** — receive alerts at 4 hours, 2 hours, 30 minutes, and 10 minutes before the bus reaches your stop, plus an arrival alarm.
- **Stop reminders** — get notified before your stop so you never sleep through it.
- **Emergency contact** — save a family member's number and they get automatically notified when you arrive.
- **Delay alerts** — instant notifications when the driver reports a delay, traffic stop, or incident.
- **Boarding confirmation** — optionally confirm when you board and when you leave the bus.
- **Trip history** — view past trips and receipts after your journey.

 
## Project Goals
 
This project serves two purposes:
 
1. **Pitch demo** — a fully working demo with realistic seeded data to present to intercity bus operators as a solution they can adopt or license.

 
## Tech Stack
 
| Layer | Technology |
|---|---|
| Backend | ASP.NET Core Web API (.NET 8) |
| Passenger Frontend | React (Vite) |
| Admin & Driver Frontend | Razor Pages  |
| ORM | Entity Framework Core (Code First) |
| Database | SQL Server |
| Realtime | SignalR |
| Authentication | JWT + OTP/PIN login |
| Maps | MapLibre GL JS + OpenFreeMap |
| SMS Notifications | Twilio (planned; not wired) |
| Hosting | Azure App Service / IIS |
 
**Why the frontend is split:**
- The passenger app is the public-facing, mobile-first showpiece of Hopon — React handles live updates, map interactions, and a smooth mobile experience naturally. 
- The admin and driver tools are internal desktop interfaces where Razor Pages is faster to build and easier to maintain.

## Run locally

Prerequisites: .NET 8 SDK/runtime (a newer SDK can also target net8.0), SQL Server, Node.js 20.19+ or 22.12+ (verified with Node 24), and npm. Keep both repositories beside each other. In this workspace they are named `backend` (Hopon.Api) and `frontend` (hopon-passenger).

### Backend

From the Hopon.Api repository root (`backend` in this workspace):

```powershell
dotnet build
# UserSecretsId is already in Hopon.Api.csproj; no init is needed.
# Replace YOUR_LOCAL_SQL_INSTANCE with your SQL Server instance, e.g. .\SQLEXPRESS.
dotnet user-secrets set "ConnectionStrings:HoponDb" "Server=YOUR_LOCAL_SQL_INSTANCE;Database=HoponDb;Trusted_Connection=True;TrustServerCertificate=True"
# Generate a local signing key in PowerShell 7. Never commit or paste it into source.
$hoponJwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:Key" $hoponJwtKey
# If dotnet-ef is not installed:
dotnet tool install --global dotnet-ef --version 8.0.18
dotnet ef database update -- --environment Development
dotnet run --launch-profile http
```

`Program.cs` reads **ConnectionStrings:HoponDb** (not DefaultConnection). JWT issuer, audience and expiry have non-secret defaults in `appsettings.json`; the signing key and connection string must be supplied through user-secrets. Development loads user-secrets automatically. Environment equivalents are `ConnectionStrings__HoponDb` and `Jwt__Key`. The example uses Windows authentication and local SQL Server certificate trust; keep real connection values outside Git. Do not overwrite an existing configured local key unnecessarily.

API: http://localhost:5101. Swagger: http://localhost:5101/swagger. Admin trips: http://localhost:5101/Admin/Trips. Driver: http://localhost:5101/Driver/Trip. The `http` launch profile sets Development and port 5101; the passenger proxy and `Hopon.Api.http` use the same port.

### Passenger

In a second terminal, from the hopon-passenger repository root (`frontend` in this workspace):

```powershell
npm install
npm run lint
npm run build
npm run dev
```

Open http://localhost:5173. Vite proxies `/api` and `/hubs` (including WebSockets) to http://localhost:5101. Keep both processes running.

Use the exact phone number of an existing local `Users` row. Request an OTP, copy the Development-only code shown on the login screen, enter it and press Enter to verify (there is currently no visible Verify button). Login opens the map view; with no active ticketed trip, use **Back to my trips** to reach the dashboard. With an active ticketed trip, **Trips** opens the dashboard and **Track on map** opens that trip. HTTP samples for OTP request/verify, my-trips and driver login are in `Hopon.Api.http`; replace the placeholders locally and do not commit tokens or credentials. The seeded driver is `driver1`; its plaintext password is not provided in the repository.

### Data and current limits

Migrations create the schema and seed three passengers plus one driver. **No seed SQL scripts or demo seeder are committed.** Stops, BusRoutes, Buses, RouteStops, TripStops and Tickets are not seeded. Trips can be created through Admin, but routes/buses must already exist, and creating a trip currently does not generate TripStops. A fresh database will not provide an end-to-end map demo; use an existing local development database with those records. Reproducible demo data belongs to Task 12.

Task 0 verification (2026-11-16): backend build, migrations, frontend lint and production build pass. Local SQL Server contained five stops, two routes, two buses, five trips and three tickets; those tickets were all for a completed trip. Login/dashboard and API map data can be checked with this data, but active tracking needs an active ticketed trip. No migration was added. The existing backend nullable warning, Vite bundle-size warning, and npm audit findings are recorded in the handoff/PR. Later security and product tasks remain pending; this is local development setup.
