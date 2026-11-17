-- ONE-OFF SQL Server data correction, NOT an EF migration.
-- Run ONLY on a database whose existing Trips/TripStops Scheduled* values were
-- entered as South African wall-clock time (SAST = UTC+2).
-- Back up the database, stop Hopon writes, and verify the stored schedule convention first.
-- Run before creating/editing schedules with the UTC version of Hopon.
-- DO NOT run on UTC data or a database mixing SAST and UTC schedules.
-- This is NOT idempotent: running it twice subtracts four hours in total.
-- Actual*, CreatedAt, GPS, ticket purchase and notification times are already UTC;
-- they must remain unchanged. Null scheduled departures remain null.
-- Select the intended database in your SQL client, then explicitly set this to 1.
DECLARE @ConfirmedSchedulesAreSast bit = 0;
IF @ConfirmedSchedulesAreSast <> 1
    THROW 50000, 'Confirm that all existing schedules are SAST before running this one-off correction.', 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE dbo.Trips
SET ScheduledDeparture = DATEADD(hour, -2, ScheduledDeparture),
    ScheduledArrival = DATEADD(hour, -2, ScheduledArrival);

UPDATE dbo.TripStops
SET ScheduledDeparture = DATEADD(hour, -2, ScheduledDeparture),
    ScheduledArrival = DATEADD(hour, -2, ScheduledArrival);

COMMIT TRANSACTION;
