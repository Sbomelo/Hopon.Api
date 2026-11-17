# Hopon

## Local setup

Requires the .NET 8 runtime (and an SDK that supports the `.slnx` solution), SQL Server, and Node/npm for the passenger frontend. Run these commands from the backend directory. Hopon's existing `UserSecretsId` is shared by local runs; `init` is safe to repeat.

```powershell
dotnet user-secrets init
# Generate a new private signing key; do not paste a key from Git history.
$jwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:Key' $jwtKey
dotnet user-secrets set 'ConnectionStrings:HoponDb' 'Server=localhost;Database=HoponDb;Trusted_Connection=True;TrustServerCertificate=True'
dotnet user-secrets set 'Admin:Username' 'your-local-admin-name'
dotnet user-secrets set 'Admin:Password' 'replace-with-a-private-strong-password'
dotnet ef database update -- --environment Development
dotnet run --launch-profile http
```

Choose your own admin credentials before running the commands. Admin settings bootstrap the first account only; changing them does not reset an existing account. Development can generate a fallback admin if credentials are absent; configure them explicitly for predictable logins. User-secrets stays outside Git and is loaded only in Development. Never place real secrets in committed appsettings files or frontend `VITE_*` settings.

Required local keys: `Jwt:Key` (at least 32 characters) and `ConnectionStrings:HoponDb`. Configure `Admin:Username` and `Admin:Password` to bootstrap an admin. Future Task 6 keys are `Twilio:AccountSid`, `Twilio:AuthToken`, and `Twilio:FromNumber`; they are not consumed yet. Non-secret JWT issuer/audience and an eight-hour lifetime remain in appsettings.json. Startup validates JWT, CORS and OTP options before staff database bootstrap. JWT expiry must be between 0.01 and 8760 hours.

In the sibling frontend directory, run `npm install` and `npm run dev`. Open `http://localhost:5173`; backend Swagger is `http://localhost:5101/swagger`. Development allows the `http://localhost:5173` CORS origin and exposes the demo OTP. Use any seeded passenger number listed in AGENTS.md. SMS delivery is still deferred. The reproducible active-trip demo remains Task 12.

### Passenger notification scheduler

Apply the `AddUniquePassengerNotifications` migration before running this version:

```powershell
dotnet ef database update -- --environment Development
```

Every 30 seconds, Hopon checks active tickets on InProgress/Delayed trips using
the passenger's Pending destination stop and its schedule-offset ETA. Passengers
who have alighted are excluded. It creates in-app reminders at 4 hours, 2 hours,
30 minutes and 10 minutes; the last reminder says to get ready to get off.
Marking a destination Arrived in the driver page/API creates an arrival alert.
Messages use the stop name and SAST ETA. Delay alerts continue to work.

If a ticket is issued late or the scheduler was offline, only the most urgent
crossed threshold is sent. Earlier thresholds are then satisfied, even if the ETA
later moves backwards. Past ETAs do not generate reminders. A filtered unique
index guards reminder/arrival `(UserId, TripId, Type)` keys against concurrent
inserts; Delay alerts can repeat. Failed inserts are isolated and logged.

For a Development demo, fast-forward only the scheduler's clock with an optional
offset (default 0):

```powershell
dotnet run --launch-profile http -- --Demo:ClockOffsetMinutes=270
```

`Demo__ClockOffsetMinutes=270` is the equivalent environment variable. Restart
Hopon after changing the value. For a destination ETA five hours from real now,
270 advances the scheduler to 30 minutes before the stop. Reset to 0 for normal
use. The offset is ignored outside Development. It does not change stored times,
the passenger ETA, JWT/OTP expiry, or the driver's arrival action.

Read the resulting feed with a passenger bearer token at
`GET http://localhost:5101/api/notifications/my`. This task generates in-app logs;
SMS, emergency-contact delivery and the passenger notification UI remain Tasks
6, 7 and 9. Legacy tickets without destination stops receive no scheduled or
destination arrival alerts. Existing SAST-only schedule data still needs the
manual correction described in [UTC time handling](docs/UTC_TIME.md).

### Hosting configuration

[.env.example](.env.example) lists environment variable names such as `Jwt__Key`, `ConnectionStrings__HoponDb`, `Admin__Username`, and `Admin__Password`. ASP.NET Core reads environment variables directly; it does not automatically read `.env`. Supply real values through the host's settings. For a separate passenger origin set `Cors__AllowedOrigins__0=https://your-passenger-host.example` (and indexed entries for more origins). Omit origin variables to deny all cross-origin requests. Do not set blank origin entries. Each origin must have HTTP/HTTPS scheme, no wildcard, path, or trailing slash. Same-origin hosting does not require CORS.

Production has an empty CORS allowlist, OTP response exposure disabled, Swagger disabled, HTTPS redirection to port 443 and HSTS. `Otp__ExposeCodeInResponse=true` remains an explicit hosted-demo opt-in. No OTP code is logged outside Development.

Forwarded headers (`X-Forwarded-For` and `X-Forwarded-Proto`) run before HTTPS handling, using ASP.NET Core's default loopback proxy trust. For Azure Linux App Service behind its managed proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and enable HTTPS Only in Azure. That platform flag broadens proxy trust: use it only where network access ensures requests come through the trusted proxy, never on a directly exposed Kestrel server. IIS uses its integration support. For another proxy, configure its actual trusted addresses instead of enabling blanket trust. See [Microsoft's proxy hosting guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).

### Configuration checks

```powershell
dotnet build
dotnet test --no-build
```

The configuration tests cover missing/short signing keys, issuer/audience/expiry validation, invalid CORS origins, Development preflight, Production denying unconfigured origins, hidden Swagger, HTTPS/HSTS and trusted forwarded headers. Existing OTP → map-data → SignalR tests cover login with the typed JWT configuration.

To check missing-key startup without changing user-secrets, start a fresh PowerShell in the backend directory:

```powershell
# Production does not load Development user-secrets. The empty command-line key
# overrides any inherited environment value and fails before database access.
dotnet run --no-build --no-launch-profile -- --environment Production --Jwt:Key=
```

Expect `Jwt:Key is required`; no signing key value is included in the validation message. To test a short key, pass `--Jwt:Key=short` and expect `Jwt:Key must contain at least 32 characters`. Resume normal local use with `dotnet run --launch-profile http` in your usual shell.

Development CORS preflight (with Hopon running):

```powershell
Invoke-WebRequest -Method Options -Uri http://localhost:5101/api/auth/otp/request -Headers @{ Origin = 'http://localhost:5173'; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'content-type' }
```

Expect HTTP 204 and `Access-Control-Allow-Origin: http://localhost:5173`. An unconfigured origin receives no allow-origin header.

### Historical credentials

Task 0 found a historical committed JWT key and a Windows-auth connection string (no SQL password). The owner must rotate any signing key derived from that value in each environment; existing tokens become invalid. Change any database password that was committed elsewhere or reused from exposed credentials; this audit did not find one in the historical Windows-auth string. Git history has not been rewritten.
 
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
