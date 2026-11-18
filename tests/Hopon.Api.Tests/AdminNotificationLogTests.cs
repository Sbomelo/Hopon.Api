using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminNotificationLogTests
{
    [Fact]
    public async Task Filters_combine_and_pages_are_stable_at_50_rows()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var user = new User { FullName = "Log passenger", PhoneNumber = "+27000000111" };
        var trip = new Trip { Bus = new Bus(), BusRoute = new BusRoute { Name = "Log route" } };
        db.Trips.Add(trip);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var timestamp = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 53; i++)
            db.NotificationLogs.Add(new NotificationLog { User = user, Trip = trip, Type = NotificationType.Delay,
                Channel = NotificationChannel.Sms, Status = NotificationStatus.Failed, CreatedAt = timestamp, Message = $"Attempt {i}" });
        db.NotificationLogs.AddRange(
            new NotificationLog { User = user, Trip = trip, Type = NotificationType.Delay, Channel = NotificationChannel.InApp, Status = NotificationStatus.Sent },
            new NotificationLog { User = user, Type = NotificationType.Delay, Channel = NotificationChannel.Sms, Status = NotificationStatus.Failed },
            new NotificationLog { UserId = 1, Trip = trip, Type = NotificationType.Delay, Channel = NotificationChannel.Sms, Status = NotificationStatus.Failed });
        await db.SaveChangesAsync();
        var service = new AdminConsoleService(db, TimeProvider.System);
        var filter = new NotificationLogFilter { TripId = trip.Id, Passenger = " passenger ", Type = NotificationType.Delay,
            Channel = NotificationChannel.Sms, Status = NotificationStatus.Failed };
        var first = await service.GetNotificationsAsync(filter);
        Assert.Equal(53, first.Total);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(50, first.Items.Count);
        Assert.Equal(first.Items.OrderByDescending(n => n.Id).Select(n => n.Id), first.Items.Select(n => n.Id));
        Assert.All(first.Items, n => { Assert.Equal("Log route", n.Route); Assert.Equal(timestamp, n.Timestamp); });
        filter.Passenger = "000000111";
        filter.PageNumber = 2;
        var second = await service.GetNotificationsAsync(filter);
        Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(n => n.Id).Intersect(second.Items.Select(n => n.Id)));
        filter.PageNumber = int.MaxValue;
        Assert.Equal(2, (await service.GetNotificationsAsync(filter)).PageNumber);
        filter.Passenger = "no such passenger";
        var empty = await service.GetNotificationsAsync(filter);
        Assert.Equal(1, empty.PageNumber);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Each_enum_filter_excludes_other_values_and_null_trip_is_supported()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        db.NotificationLogs.AddRange(
            new NotificationLog { UserId = 1, Type = NotificationType.Delay, Channel = NotificationChannel.Sms, Status = NotificationStatus.Failed },
            new NotificationLog { UserId = 1, Type = NotificationType.Arrival, Channel = NotificationChannel.InApp, Status = NotificationStatus.Pending });
        await db.SaveChangesAsync();
        var service = new AdminConsoleService(db, TimeProvider.System);
        Assert.Single((await service.GetNotificationsAsync(new() { Type = NotificationType.Arrival })).Items);
        Assert.Single((await service.GetNotificationsAsync(new() { Channel = NotificationChannel.Sms })).Items);
        Assert.Single((await service.GetNotificationsAsync(new() { Status = NotificationStatus.Pending })).Items);
        Assert.All((await service.GetNotificationsAsync(new())).Items, n => Assert.Null(n.Route));
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
            () => service.GetNotificationsAsync(new() { PageNumber = 0 }));
    }
}
