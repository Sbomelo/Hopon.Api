# Task 11 — privacy operations

Branches: `task-11-location` → `task-11-audit` → `task-11-privacy-ops`. Scope: Task 11 only, delivered as three ordered PRs. The first targets `task-10-admin-console` (Task 10 PR #16); merge that dependency first.

## Behavior and assumptions

- Driver `/Driver/Trip` starts with automatic sharing off. Enabling it watches GPS and posts the latest fresh fix every 10 seconds, with a visible indicator and Stop sharing control. Navigation, permission errors and rejected fixes stop sharing. Fetch uses the existing staff cookie and antiforgery token. An assigned-trip ID prevents an old tab posting to a newly assigned trip. Manual sharing is retained.
- Singleton `ILocationThrottle` serializes each trip through bounded striped locks. Fixes less than 3 seconds apart get 429; invalid ranges/non-finite coordinates and speeds above 250 km/h get 400. Server receipt time is authoritative. Distance is used only to validate jumps; ETA remains schedule-offset based. Every accepted fix is sent to SignalR; persistence is at most once per 15 seconds. Page-load map data can therefore be up to 15 seconds old while fixes arrive regularly; live updates are unaffected. With no new fixes, the last fix can of course be older. On restart the latest database fix restores the persistence window and plausibility baseline. Intermediate, unpersisted fixes cannot survive restart. Throttling/audit serialization is for a single Hopon server, matching the current hosting design.
- Audit stores only integer passenger/trip IDs, action and UTC time. No IP or device fields. Successful map/history/feed reads and live joins are recorded, at most once per exact `(UserId, TripId, Action)` in a rolling five-minute period; the exact five-minute boundary is allowed. Notifications use null TripId because the feed spans trips. Unauthorized/failed reads are not recorded. A separate scoped context and catch/log preserve request behavior on audit failure. The persisted audit query also avoids duplicate entries across restarts. The admin page is read-only, filterable and paged at 50, ordered by time and ID.
- `Retention:Enabled=false` prevents every retention operation, including database reads and admin previews. Only Production configuration enables the daily service; dry run stays true on first deploy. Admin can request a dry run only; it cannot turn on deletion or override disabled mode.
- Daily run is at 02:00 SAST (00:00 UTC), while the process is running. No catch-up unless RunOnStartup is enabled. `DaysAfterTrip=90`, `LocationDays=30`, `DryRun=true`, `RunOnStartup=false`; positive day values validated at startup. After reviewing dry-run counts, the operator can explicitly set `Retention__DryRun=false` in deployment configuration.
- Expired trip means Completed/Cancelled and `(ActualArrival ?? ScheduledArrival)` strictly before the 90-day cutoff. Cancelled trips lack a cancellation timestamp, so scheduled arrival is the conservative fallback. Recent trips, including inactive tickets on them, protect passenger identity; unfinished trips also protect it even with stale schedules. User creation must be strictly before cutoff. Already anonymised users are skipped. Old users without any tickets are eligible.
- Deletes use ExecuteDeleteAsync in batches of 500: all locations older than 30 days; expired-trip boarding/notification/audit logs; OTP requests older than two days. Audit entries older than the trip retention window are also purged, including null-trip feed reads, so these cannot grow forever. Exact cutoff timestamps stay. Trips and tickets stay.
- Anonymisation batches recheck eligibility in a serializable transaction, remove emergency contacts, and set name to `Deleted user`, a unique `deleted-{Id}-{Guid:N}` phone token and IsActive false. There are no demo-ID exemptions: disabled retention protects all demo data. Live deletion is never exercised against the local demo database during verification.
- Last successful run time and counts are kept in memory, resetting on restart. Logs include counts/mode only. Failures do not log exceptions or personal data and the scheduler retries the next day. Partial deletion before failure is possible; operations are idempotent. A failed run does not replace the last successful summary.

## Migration and run commands

New migration: `20261118072917_AddAccessAuditLog`. Adds AccessAuditLogs and `(UserId, CreatedAt)` index; no existing migration changed. Audit IDs are scalar references rather than cascade FKs so business-record deletion cannot unexpectedly remove the audit. No new package dependency.

From `C:/dev/my-project/backend`:

```powershell
dotnet build
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj --filter "FullyQualifiedName~PrivacyOps"
node --test tests/Hopon.Api.Tests/driver-location.test.cjs
dotnet ef database update -- --environment Development
dotnet ef migrations has-pending-model-changes -- --environment Development
dotnet run --launch-profile http
```

From `C:/dev/my-project/frontend`:

```powershell
npm run lint
npm run build
npm run dev
```

Sign in at `http://localhost:5101/Staff/Login`. Admin URLs: `/Admin/Audit` (e.g. `?UserId=1&TripId=1&ActionFilter=ViewedMap&PageNumber=1`) and `/Admin/Retention`. Driver URL: `/Driver/Trip`. In an isolated active trip, enable auto-sharing, check indicator and ten-second posts, then Stop sharing and navigate away. Passengers should receive every accepted fix. With a driver JWT, POST `/api/driver/trips/{tripId}/location` with JSON `{ "latitude": -29.85, "longitude": 31.02 }`: repeat inside three seconds for 429; a >250 km/h jump after that window gets 400. Do not move the real demo bus for acceptance.

In an isolated database enable retention with DryRun=true, review `/Admin/Retention` counts, then use DryRun=false for actual deletion checks. Never turn live deletion on against the demo database merely to verify it. Confirm recent/cutoff/unfinished records stay, expired operational rows disappear, unique phone tokens replace expired identities, contacts are removed, and rerunning is idempotent.

## Verification on 2026-11-18

- Backend build: zero warnings/errors. 25 new .NET cases cover fake-clock boundaries, concurrent throttling, jump checks, persisted restart baseline, broadcast-failure persistence, audit deduplication/failure, retention disabled/dry/live/batching/idempotence, HTTP status/access/audit and admin filter/paging behavior. All 25 passed; all 443 backend tests then passed with zero failures/skips. The full run found a missing throttle registration in the older isolated scheduler test fixture; it was updated, all 15 scheduler tests passed, then the full suite passed. Earlier Application Control loader/discovery failures cleared on ordinary rebuild/retry without policy changes. A zero exit code with no discovered tests was never counted as a pass.
- Five Node driver-sharing tests passed (toggle/timing/stop, stale data, 429, rejection, navigation/permission cleanup).
- Frontend lint/build passed; existing Vite bundle-size warning remains. Passenger source/themes unchanged; its demo retention copy remains accurate because automatic deletion is disabled in Development.
- Migration applied to local SQL Server; no pending model changes. Hopon starts on port 5101 in Development with retention disabled, using the normal `dotnet bin/Debug/net8.0/Hopon.Api.dll --urls http://localhost:5101 --environment Development` host (the generated executable was intermittently blocked). No real SMS sent or demo trip/ticket/GPS data modified.
- Headless Edge: both admin pages returned 200 and fit 360px/1366px in both themes (eight combinations), no JavaScript errors; disabled dry-run POST showed the correct message. Mobile light Audit/dark Retention screenshots inspected. Temporary QA admin and its credential file removed. Device GPS/live sharing has automated server and script coverage; a real device field check remains an optional manual acceptance step above. No real driver GPS was collected during verification.
