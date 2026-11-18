using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminMonitorTests
{
    [Fact]
    public async Task Monitor_uses_latest_fix_pending_stop_and_schedule_offset_final_ETA()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var trip = new Trip { Status = TripStatus.Delayed, Bus = new Bus { RegistrationNumber = "MONITOR" },
            BusRoute = new BusRoute { Name = "Monitor route" }, ScheduledDeparture = now.UtcDateTime,
            ActualDeparture = now.UtcDateTime.AddMinutes(10), ScheduledArrival = now.UtcDateTime.AddHours(2) };
        trip.TripStops.Add(new TripStop { SequenceOrder = 1, Status = TripStopStatus.Departed, Stop = new Stop { Name = "Origin" } });
        trip.TripStops.Add(new TripStop { SequenceOrder = 2, Stop = new Stop { Name = "Next" }, ScheduledArrival = trip.ScheduledArrival });
        trip.LocationUpdates.Add(new LocationUpdate { Latitude = -29, Longitude = 30, RecordedAt = now.UtcDateTime.AddMinutes(-9) });
        trip.LocationUpdates.Add(new LocationUpdate { Latitude = -28, Longitude = 31, RecordedAt = now.UtcDateTime.AddMinutes(-6) });
        db.Trips.AddRange(trip, new Trip { Status = TripStatus.InProgress, Bus = new Bus(), BusRoute = new BusRoute() },
            new Trip { Status = TripStatus.Scheduled, Bus = new Bus(), BusRoute = new BusRoute() });
        await db.SaveChangesAsync();
        var rows = await new AdminConsoleService(db, new FakeTimeProvider(now)).GetMonitorAsync();
        Assert.Equal(2, rows.Count);
        var row = rows.Single(t => t.TripId == trip.Id);
        Assert.Equal(-28, row.Latitude);
        Assert.Equal(6, row.AgeMinutes);
        Assert.Contains("mlat=-28&mlon=31", row.LocationUrl);
        Assert.Equal("Next", row.NextStop);
        Assert.Equal("18 Nov 2026, 14:10", row.FinalEtaSast);
        Assert.Null(rows.Single(t => t.TripId != trip.Id).AgeMinutes);
    }
}
