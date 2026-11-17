# UTC storage and South African display time

Hopon stores and returns UTC timestamps. Admin `datetime-local` values are SAST
(UTC+2, no daylight saving). `SaTime.ToUtc` subtracts two hours from Unspecified
or Local wall-clock values, independently of the server timezone; an already UTC
value is returned unchanged. `SaTime.ToSa` adds two hours and returns Unspecified
for Razor display and Edit inputs. Services continue to receive UTC values.

The EF conventions apply `UtcDateTimeConverter` to required and nullable dates.
It preserves writes and marks materialized values UTC without changing ticks.
Null stays null. There is no schema migration or automatic data correction.

The passenger formatters in `src/utils/format.js` always select
`Africa/Johannesburg`, including GPS and emergency-contact dates. My-trips
`tripDate` now carries the departure instant instead of a truncated UTC midnight;
this preserves the correct SAST date for departures just after midnight.

## Existing schedules

The converter cannot tell whether an old database value was entered as SAST.
Old SAST schedules must be corrected before they can give accurate UTC ETAs.

Use [the one-off SQL script](sql/shift-scheduled-times-to-utc.sql) only on a
database where **all existing schedule values are SAST**. Back up the intended
database and stop writes before running it, before creating or editing schedules
with the UTC version. Inspect the script and explicitly set its confirmation
variable to 1. Record that the correction was run; it must never be run twice.
For mixed SAST/UTC schedules, identify the affected rows separately instead of
running the whole-database script.

The script updates only `Trips` and `TripStops` `ScheduledDeparture` and
`ScheduledArrival`, together in one transaction. Actual times, GPS timestamps,
purchase dates and other audit timestamps remain unchanged. The script is not
run by startup or migrations and was not executed during Task 4b.

## Verification

From backend:

```powershell
dotnet build
dotnet test --no-build
dotnet ef migrations has-pending-model-changes --no-build -- --environment Development
dotnet ef database update --no-build -- --environment Development
dotnet run --launch-profile http
```

From frontend:

```powershell
node --test tests/*.test.mjs
npm run lint
npm run build
npm run dev
```

Sign in at `http://localhost:5101/Staff/Login`. Create a trip at 18:00 SAST;
the database/API departure should be 16:00Z and Edit should still show 18:00.
An actual departure of 16:10Z produces a ten-minute ETA offset. Admin/driver
views and the passenger dashboard display SAST, including on devices in other
timezones. A 00:30 SAST departure belongs to the following day relative to its
22:30Z timestamp. API timestamps loaded from the database end in `Z`.
