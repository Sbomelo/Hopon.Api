using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminHomeTests
{
    [Fact]
    public async Task Home_uses_SAST_midnight_and_a_rolling_notification_window()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var now = new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero);
        var today = now.UtcDateTime.Date.AddHours(-2);
        var trip = new Trip { Bus = new Bus(), BusRoute = new BusRoute(), ScheduledDeparture = today };
        db.Trips.AddRange(trip,
            new Trip { Bus = new Bus(), BusRoute = new BusRoute(), ScheduledDeparture = today.AddTicks(-1) },
            new Trip { Bus = new Bus(), BusRoute = new BusRoute(), ScheduledDeparture = today.AddDays(1) },
            new Trip { Bus = new Bus(), BusRoute = new BusRoute(), Status = TripStatus.InProgress },
            new Trip { Bus = new Bus(), BusRoute = new BusRoute(), Status = TripStatus.Delayed });
        db.Tickets.Add(new Ticket { Trip = trip, UserId = 1, TicketReference = "HOME", PurchaseDate = today, IsActive = false });
        db.NotificationLogs.AddRange(
            new NotificationLog { UserId = 1, Type = NotificationType.Delay, Status = NotificationStatus.Sent, CreatedAt = now.UtcDateTime.AddHours(-24) },
            new NotificationLog { UserId = 1, Type = NotificationType.Delay, Status = NotificationStatus.Failed, CreatedAt = now.UtcDateTime },
            new NotificationLog { UserId = 1, Type = NotificationType.Delay, Status = NotificationStatus.Sent, CreatedAt = now.UtcDateTime.AddHours(-24).AddTicks(-1) });
        await db.SaveChangesAsync();
        var counts = await new AdminConsoleService(db, new FakeTimeProvider(now)).GetHomeAsync();
        Assert.Equal(1, counts.ScheduledToday);
        Assert.Equal(1, counts.InProgress);
        Assert.Equal(1, counts.Delayed);
        Assert.Equal(1, counts.TicketsSoldToday);
        Assert.Equal(1, counts.NotificationsSent);
        Assert.Equal(1, counts.NotificationsFailed);
    }
}
