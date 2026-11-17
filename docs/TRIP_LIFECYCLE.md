# Trip lifecycle (Task 8)

Admin Razor handlers, driver Razor handlers and the driver API all use
`ITripLifecycleService` / `TripLifecycleService`. Existing method names are kept;
`CancelAsync(Trip trip, string? reason)` is added. Callers load a tracked Trip in
the same scoped HoponDbContext. Admin handlers use `FindAsync(id)` and return 404
for missing IDs. The old admin `UpdateTripStatusAsync` is removed.

`TripStatusRules.CanTransition` allows only:

| From | To |
|---|---|
| Scheduled | InProgress, Cancelled |
| InProgress | Delayed, Completed, Cancelled |
| Delayed | InProgress, Completed, Cancelled |
| Completed / Cancelled | None |

Start requires Scheduled and records UTC ActualDeparture. Resume requires Delayed
and preserves ActualDeparture. Completion records UTC ActualArrival and retains
Task 7's final-stop emergency-contact fallback. Delay sends the existing passenger
delay message. Cancellation preserves existing Actual times and creates
`Cancellation` notifications for active ticket holders of that trip with exactly
`Your trip has been cancelled.` followed by a space and the trimmed reason when
one is given. NotificationType.Cancellation is appended as integer 7; all previous
values remain unchanged. No schema migration is needed.

Every successful lifecycle action saves and sends `ReceiveTripStatusUpdate` to
`trip-{id}`, including status, reason and Actual times. Invalid transitions return
an error without saving, notifying or broadcasting. Repeated Delayed -> Delayed
and Cancelled -> Cancelled requests are rejected. Report a later delay by resuming
first. Admin errors survive redirects using TempData.

On Admin Edit, an InProgress trip has a delay reason field and Mark as Delayed;
a Delayed trip has Resume; either running state has Mark as Completed. On Admin
Details, expand Cancel Trip, enter an optional reason and confirm cancellation.
The server validates the transition even when the form is stale or a POST is
crafted directly.

Cancellation uses the existing channel policy. InApp is always requested; SMS is
requested through the same notification service but requires both SmsEnabled and
`Cancellation` in Notifications:SmsTypes. It is excluded from the default SMS
allowlist. No new paid sends are enabled by default. Once per holder means one
notification per enabled channel on the successful transition; subsequent
cancellation attempts are rejected. Existing service delivery semantics remain:
database changes, notifications and SignalR are separate operations, without an
outbox, cross-request concurrency claim or automatic retry.

## Verification

```powershell
cd C:\dev\my-project\backend
dotnet build
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj
dotnet test tests/Hopon.Api.Tests/Hopon.Api.Tests.csproj --no-build --filter FullyQualifiedName~TripLifecycleTests
dotnet ef database update --no-build -- --environment Development
dotnet ef migrations has-pending-model-changes --no-build -- --environment Development
dotnet run --no-build --launch-profile http

cd C:\dev\my-project\frontend
npm run lint
npm run build
node --test tests/*.test.mjs
npm run dev
```

The focused tests cover all 25 status pairs, lifecycle persistence and rejection,
start/resume timestamp behavior, active versus voided tickets, cancellation with
and without reasons, opt-in SMS and repeated cancellation. HTTP tests compare
real admin Razor and driver API delay NotificationLogs and captured SignalR
payloads, and exercise admin resume, completion emergency fallback, cancellation,
errors and missing IDs. All SMS uses fakes.

For a manual check, sign in as Admin at http://localhost:5101/Staff/Login, open
`/Admin/Trips/Edit/{id}` for an InProgress trip with active tickets, enter Traffic
and mark delayed. The passenger-token `GET /api/notifications/my` should show
`Your bus has been delayed: Traffic`. Resume from Edit, then complete; Details
should show the preserved ActualDeparture and new ActualArrival. On a separate
Scheduled or running trip, use Details -> Cancel Trip and enter Bus unavailable;
each active holder should get `Your trip has been cancelled. Bus unavailable`.
The passenger map receives status changes through the same SignalR event as driver
actions. Attempting completion of a Scheduled trip or cancellation of a terminal
trip must show an error and leave status unchanged.

Assumptions: delay and cancellation reasons are optional. Cancellation does not
invent an arrival/departure or deactivate tickets. Admin Start and new driver
cancellation routes are outside Task 8; existing driver Start remains available.
Inline fix: nullable driver API delay reasons become the existing empty-reason
message instead of passing null to the lifecycle contract.
