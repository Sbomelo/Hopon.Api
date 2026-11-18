# Task 9d — Passenger history and destination map data

Companion to the five frontend commits in `hopon-passenger` branch `task-9-passenger-ui`. No migrations or access-rule changes for tracking.

`GET /api/trips/{tripId}/history` keeps Passenger authorization but now relies on TripHistoryService's user/ticket ownership query instead of the active-ticket filter. An inactive owned ticket can read history; another passenger gets 404. Inline fix: my-trips includes inactive tickets in Past trips so their history is reachable, while Live trips still require an active ticket and `TripStatusRules.IsTrackingActive`.

`GET /api/trips/{tripId}/map-data` still uses `[RequireTripAccess]`. The controller passes the authorized ticket's destination ID to ITripMapService. That service selects it only among that trip's stops and exposes nullable DestinationStopName/DestinationEstimatedArrival using EtaCalculator.CalculateStopEta. Caller query parameters cannot override the destination. Legacy null destinations remain null. No location is added to history.

## Verification

From `C:\dev\my-project\backend`:

```powershell
dotnet build
dotnet test --no-build --filter FullyQualifiedName~PassengerUiTests
dotnet test --no-build
dotnet ef database update --no-build -- --environment Development
dotnet ef migrations has-pending-model-changes --no-build -- --environment Development
dotnet run --no-build --launch-profile http
```

Build/test project compile with zero warnings/errors. Four new HTTP/service cases cover inactive Scheduled/Completed/Cancelled history, its appearance in Past trips, other-user 404, anonymous 401, driver 403, retained active-ticket restrictions on map/boarding/access, ticket-specific destinations, query-string spoofing, null legacy tickets, foreign-trip destinations and offset versus actual stop ETA.

All 382 backend tests passed, including the four new cases. SQL Server is already up to date with no pending model changes. Initial Windows Application Control `0x800711C7` loader/startup failures recovered on a normal retry; no policy was changed. Backend HTTP 5101 and Vite 5173 are running. Local Logging-mode OTP request/verify, my-trips, notifications, emergency contact and owned past-trip history passed. The existing local passenger has no live trip, so map/boarding interaction remains covered by isolated integration tests rather than a local live-ticket smoke check. No real SMS or demo trip/ticket edits.

Frontend lint/build and all seven Node tests passed. Visual acceptance at 360px/desktop in both themes is pending: in-app browser creation timed out; native Edge inspection failed on its minimized window. Complete the manual steps before accepting the draft stack. Existing Vite chunk-size warning remains.

## Manual checks

Start backend on `http://localhost:5101` and frontend on `http://localhost:5173`. Log in as a seeded passenger with an existing ticket. At 360px and desktop width, in both themes:

1. My trips → Past trips → a card opens route, ticket/seat, scheduled/actual SAST times, boarding events and the ordered stop timeline. An inactive ticket must remain in Past trips and open successfully. Fetch the history URL with another passenger's token: 404. Anonymous: 401; driver token: 403.
2. Open live tickets for passengers with different destinations on the same trip: each map shows their own destination and ETA. Append `?destinationTripStopId=<another-stop>` to map-data: response is unchanged. Inactive/no-ticket map requests still return 404.
3. Start ten minutes late and mark a destination arrived using `/Driver/Trip`: frontend refreshes on SignalR events, ETA reflects departure offset, then actual arrival. Legacy null destinations display a dash.

The frontend documentation has the remaining 9a–9e click paths. SOS is an explicit call-link sheet only; it never calls or messages automatically. The login notice is draft copy requiring legal review and an owner-supplied privacy contact. Retention implementation remains Task 11.
