# Task 10 — Admin console

Task 1 staff authentication is merged. All pages below inherit the existing `/Admin` folder's `AdminOnly` cookie policy. Passenger and driver JWTs do not open these pages. No EF queries are made in the new PageModels; the touched trip Details/Edit handlers also delegate database work to services.

## Pages and behavior

| URL | Behavior |
| --- | --- |
| `/Admin` or `/Admin/Index` | Post-login home; in-progress/delayed counts, Scheduled departures today, tickets purchased today and Sent/Failed notification attempts created in the last 24 hours. |
| `/Admin/Monitor` | InProgress/Delayed trips, latest GPS fix and age, next Pending stop and final-stop ETA in SAST. More than five minutes old, or no fix, is highlighted. Coordinates link to OpenStreetMap; Details opens the trip. |
| `/Admin/Monitor?handler=Trips` | Admin-only, no-store JSON used by `wwwroot/js/monitor.js` every 15 seconds. Failed fetches preserve the last snapshot and retry; expired/denied sessions stop polling. No full-page reload. |
| `/Admin/Notifications` | Read-only, newest first, 50 per page. Trip, passenger name/phone contains, type, channel and status filters combine. Timestamp means CreatedAt, displayed in SAST. Paging preserves filters. |
| `/Admin/Trips/Details/{id}` | Scheduled tickets show Void or Reinstate with confirmation. Other statuses reject both actions in the service, even for a forged POST. |
| `/Admin/Buses` and `/Admin/Buses/Edit/{id?}` | List, create, edit and confirmed delete. Required unique registration, optional model and positive capacity. |
| `/Admin/Stops` and `/Admin/Stops/Edit/{id?}` | List, create, edit and confirmed delete. Required name and valid coordinates; an OpenStreetMap link checks the saved point. |
| `/Admin/Routes` and `/Admin/Routes/Edit/{id?}` | Name CRUD plus append, remove, move up and move down. Contiguous 1-based sequences are saved atomically without violating the unique route/sequence index. |

The shared admin nav links every new page as well as Trips and Drivers. Both themes use existing CSS tokens; no theme token values changed. Wide tables scroll inside their container at phone widths.

## Ticket access and route rules

- Void preserves the ticket reference, passenger, seat and stop assignments. Re-issuing to the same passenger remains blocked by the existing user/trip unique key; use Reinstate.
- Reinstate checks active passenger, available capacity and available seat. It reactivates the original row. Repeated Void/Reinstate requests are idempotent while Scheduled.
- Ticket issuance and activation changes use serializable transactions to protect capacity/seat checks. Existing active-ticket recipient filters exclude voided tickets from reminders, arrival, delay, cancellation and emergency alerts.
- Database trip access checks reject voided tickets. `TripTrackingSessions` also removes their already-open SignalR connections from `trip-{id}`. Joins and revocations are serialized, and disconnects release registry entries. This registry matches Hopon's existing single-server deployment; no backplane is introduced.
- A route referenced by **any** trip, including Completed/Cancelled, cannot have stops reordered or removed. The UI explains the schedule/destination dependency, and services enforce it. Append is allowed for future trips; existing TripStops are unchanged. Route creation can start empty so stops can be added individually; creating a trip still requires at least two usable stops.
- Trip creation already generated TripStops through `TripStopSchedule`. It now reads the route and saves the trip inside a serializable transaction, coordinating with route edits. No legacy routes or trip stops are backfilled.
- Referenced buses, stops and routes return friendly deletion errors. Services precheck references and catch `DbUpdateException` for constraint races.

## Migration

New migration: `20261118064058_AddUniqueBusRegistration`. No previous migration was changed. EF narrows RegistrationNumber from nvarchar(max) to nvarchar(450) for its unique index. The index excludes legacy empty registrations; new/edited registrations are required and limited to 30 characters. Writes trim and uppercase registration numbers; service duplicate checks also compare normalized legacy values.

Before applying to another database, reconcile duplicate nonempty registrations and values longer than 450 characters:

```sql
SELECT UPPER(LTRIM(RTRIM(RegistrationNumber))) AS Registration, COUNT(*) AS Copies
FROM Buses WHERE RegistrationNumber <> ''
GROUP BY UPPER(LTRIM(RTRIM(RegistrationNumber))) HAVING COUNT(*) > 1;

SELECT Id FROM Buses WHERE LEN(RegistrationNumber) > 450;
```

Both local preflight counts were zero; the migration applied successfully. Existing trips, tickets, routes, schedules and GPS rows were not edited for verification.

## Verification commands

From `backend`:

```powershell
dotnet build
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj
dotnet ef database update -- --environment Development
dotnet ef migrations has-pending-model-changes -- --environment Development
dotnet run --launch-profile http
```

From `frontend`:

```powershell
npm run lint
npm run build
npm run dev
```

Open `http://localhost:5101/Staff/Login` and sign in as an admin. The landing page is `/Admin`. For isolated service/security coverage, use:

```powershell
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj --filter "FullyQualifiedName~AdminHomeTests|FullyQualifiedName~AdminMonitorTests|FullyQualifiedName~AdminNotificationLogTests|FullyQualifiedName~AdminTicketTests|FullyQualifiedName~AdminMasterDataTests|FullyQualifiedName~AdminConsoleHttpTests"
```

Manual acceptance:

1. Verify the home counts and links. Today means the SAST calendar day. Tickets sold includes later-voided purchases; notification counts use the attempts' creation window, regardless of SentAt.
2. Start/delay a test trip using the existing driver flow. Verify the monitor's GPS age, next Pending stop and schedule-offset ETA. Wait 15 seconds after another GPS update; the table updates without a navigation. Check stale and missing fixes, empty monitor, network failure and re-login behavior.
3. Combine notification filters and use Next/Previous. Check SAST timestamps, escaped message text and 50-row pages. Invalid query values show validation errors.
4. On a Scheduled test trip, void a ticket while its passenger map is open. Map-data/JoinTrip access and group delivery stop. Reinstate restores access unless the passenger is inactive, the bus is full or the seat was resold. Verify both actions are rejected after a trip starts or becomes Delayed.
5. Create a bus and two stops, check each point on OSM, then create a route and append its stops. Add a third stop, move/remove stops and verify order is contiguous. Create a trip from the route and verify generated TripStops. Try a forged reorder/remove POST afterward: both are rejected with the explanation. Referenced deletions show friendly errors.
6. Check 360px and desktop, light/dark, nav wrapping, table scrolling and theme persistence.

Automated HTTP tests use SQLite and synthetic accounts. Browser checks use headless Edge at 1366px and 360px, both themes, against rendered SQL Server pages; monitor update/error/empty/XSS checks use synthetic JSON responses rather than editing demo trip data. The temporary QA admin is removed after review. No paid SMS is sent. New test coverage includes a real WebSocket join, group delivery before void, revocation and denied rejoin.

Final results (2026-11-18): backend rebuild **0 warnings / 0 errors**, **418 tests passed / 0 skipped**, frontend lint/build passed, SQL migration applied and no pending model changes. The 28 rendered page/theme/viewport combinations and monitor browser checks passed. Existing Vite bundle-size warning remains. Windows Application Control intermittently blocked earlier attempts; ordinary rebuild/retry and the normal `dotnet` host restored execution without policy changes. The browser connector timed out, so rendered QA used headless Edge.

## Delivery assumptions and inline fixes

- Filter substring matching follows the configured database collation.
- Final-stop ETA uses `EtaCalculator.CalculateStopEta`; a legacy trip with no TripStops falls back to the existing trip-level schedule-offset ETA.
- Bus capacity cannot be reduced below a trip's existing active ticket count.
- Inline fixes: separate form validation prevents Issue Ticket requirements from blocking Void/Reinstate; trip PageModels no longer load EF entities; duplicate-registration test fixtures use distinct registrations; the Hopon brand and plain links remain visible in dark mode; monitor status/action labels do not split across lines.
- Task 11 audit/retention, Task 12 simulator/demo seeding and real SMS delivery are unchanged.
