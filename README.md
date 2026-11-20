# Hopon

[![Backend CI](https://github.com/Sbomelo/Hopon.Api/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Sbomelo/Hopon.Api/actions/workflows/ci.yml)
 
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
| SMS Notifications | Logging (default) / Twilio (opt-in) |
| Hosting | Azure App Service / IIS |
 
**Why the frontend is split:**
- The passenger app is the public-facing, mobile-first showpiece of Hopon — React handles live updates, map interactions, and a smooth mobile experience naturally. 
- The admin and driver tools are internal desktop interfaces where Razor Pages is faster to build and easier to maintain.

## Testing

From this repository root with the .NET 8 SDK, run `dotnet test`. The xUnit suite uses SQLite in memory and simulated SMS delivery.

Run the driver browser-script tests with Node.js 22 using `node --test tests/Hopon.Api.Tests/driver-location.test.cjs`.

## Run the demo

Use the Hopon.Api and hopon-passenger repositories together. Install the .NET 8 SDK/runtime, SQL Server (Express works), the EF CLI, Node.js 20.19+ or 22.12+, and npm. The basemap needs internet access. The demo road and GPS trail come from Hopon.

In the backend repository, configure a local database and signing key once. This PowerShell 7 example uses a dedicated demo database on SQL Express. Substitute your instance name as needed.

```powershell
dotnet tool install --global dotnet-ef --version 8.0.18  # first installation only
dotnet user-secrets set "ConnectionStrings:HoponDb" "Server=.\SQLEXPRESS;Database=HoponDemo;Trusted_Connection=True;TrustServerCertificate=True"
$demoKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:Key" $demoKey
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update
dotnet run -- --seed-demo
dotnet run
```

Seeding exits after creating the live trip and yesterday’s trip. On first server startup, Development prints the generated staff login once. Use that login for the simulator. You can also supply your own initial `Admin:Username` and `Admin:Password` through user-secrets before first startup. The Development defaults show the OTP on screen and use Logging SMS.

In a second terminal in the passenger repository, run:

```powershell
npm ci
npm run dev
```

Open the passenger view and request a code for **+27600000001**. Enter the six digits displayed on the login screen and choose **Verify code**. Passenger 1 has a sample emergency contact. **+27600000002** boards at Pietermaritzburg. All three demo tickets have seats to Johannesburg Park Station. [Passenger view](http://localhost:5173)

Open the simulator in a second window, sign in as Admin and select the demo trip. **Auto-drive** moves about 92 metres every three seconds. **Move bus +10 km** fast-forwards along the N3. **Jump to next stop** sends a fix and records arrival. **Depart stop** and **Complete trip** finish the journey. Each accepted fix reaches the passenger map via SignalR. [Admin simulator](http://localhost:5101/Admin/Simulator)

For another pitch, stop the server and run `dotnet run -- --reset-demo`, then `dotnet run`. This restarts the schedule relative to now. Ordinary seeding preserves progress. Reset recognises trips by the demo route, coach and reserved ticket references. When a finished trip has other passenger records, reset keeps the trip and its shared history and replaces the seeded passenger records. Finish any active demo trip with other passenger records before resetting. To seed on Development startup, set `Demo:SeedOnStartup` to `true`.

### What to show in 60 seconds

| Time | Action |
|---|---|
| 0–15 s | Log in as Passenger 1. Point out the bus near Villiers, the trail and the Park Station ETA about two hours away. |
| 15–30 s | Enable **Follow bus** on the map and **Auto-drive** in the simulator. Watch two updates, then turn Auto-drive off. |
| 30–45 s | Enter “Traffic near Villiers”, choose **Trigger delay**, show the passenger status and notification, then **Resume**. |
| 45–60 s | **Jump to next stop**. Open the passenger notifications to show arrival, then open yesterday’s trip under **Past trips** for seats, boarding and alighting. |

For a reminder demonstration, set `Demo:ClockOffsetMinutes` to `91` in Development and restart after a fresh reset. Within the next scheduler scan (30 seconds), the two-hour ETA crosses the 30-minute reminder threshold. Restore the setting to zero for the standard pitch.

The five road-access pins use Intercape’s published boarding locations snapped to OpenStreetMap roads by the public OSRM driving service. The committed line retains the five stop vertices and the N3 fix at −27.031420, 28.618667. It simplifies 6,718 points to 230. Road data © OpenStreetMap contributors, ODbL. [Operator stops](https://www.intercape.co.za/bus-stops/) · [Villiers on OpenStreetMap](https://www.openstreetmap.org/?mlat=-27.031420&mlon=28.618667#map=16/-27.031420/28.618667) · [OpenStreetMap licence](https://www.openstreetmap.org/copyright)
