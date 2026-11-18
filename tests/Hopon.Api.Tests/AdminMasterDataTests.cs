using System.ComponentModel.DataAnnotations;
using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminMasterDataTests
{
    [Fact]
    public async Task Route_add_move_remove_keep_order_contiguous_and_unique()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = new AdminMasterDataService(db);
        var route = await service.SaveRouteAsync(null, new() { Name = " Ordered route " });
        var stopIds = new List<int>();
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            var stop = await service.SaveStopAsync(null, new() { Name = name, Latitude = 0, Longitude = stopIds.Count });
            stopIds.Add(stop.Id);
            Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.Add, stop.Id)).Success);
        }
        var rows = (await service.GetRouteAsync(route.Id))!.Stops;
        var originalIds = rows.Select(s => s.Id).ToList();
        AssertOrder(rows, "A", "B", "C", "D");
        Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.MoveUp, rows[2].Id)).Success);
        AssertOrder((await service.GetRouteAsync(route.Id))!.Stops, "A", "C", "B", "D");
        Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.MoveDown, rows[0].Id)).Success);
        AssertOrder((await service.GetRouteAsync(route.Id))!.Stops, "C", "A", "B", "D");
        Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.Remove, rows[1].Id)).Success);
        var final = (await service.GetRouteAsync(route.Id))!.Stops;
        AssertOrder(final, "C", "A", "D");
        Assert.Equal(originalIds.Where(id => id != rows[1].Id).Order(), final.Select(s => s.Id).Order());
        Assert.False((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.Add, stopIds[0])).Success);
        Assert.False((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.MoveUp, final[0].Id)).Success);
        Assert.False((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.MoveDown, final[^1].Id)).Success);
        var other = await service.SaveRouteAsync(null, new() { Name = "Other" });
        Assert.False((await service.ChangeRouteStopAsync(other.Id, RouteStopAction.Remove, final[0].Id)).Success);
        AssertOrder((await service.GetRouteAsync(route.Id))!.Stops, "C", "A", "D");
        foreach (var row in final) Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.Remove, row.Id)).Success);
        Assert.Empty((await service.GetRouteAsync(route.Id))!.Stops);
        Assert.True((await service.DeleteRouteAsync(route.Id)).Success);
        Assert.Null(await service.GetRouteAsync(route.Id));
    }

    [Theory]
    [InlineData(TripStatus.Scheduled)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.Delayed)]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.Cancelled)]
    public async Task Any_trip_locks_removal_and_order_but_allows_append_for_future_trips(TripStatus status)
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = new AdminMasterDataService(db);
        var route = new BusRoute { Name = "Used", RouteStops = new List<RouteStop> {
            new() { SequenceOrder = 1, Stop = new Stop { Name = "A", Latitude = 0, Longitude = 0 } },
            new() { SequenceOrder = 2, Stop = new Stop { Name = "B", Latitude = 0, Longitude = 1 } } } };
        var bus = new Bus { RegistrationNumber = "USED", Capacity = 10 };
        db.AddRange(route, bus);
        await db.SaveChangesAsync();
        var tripId = await new AdminTripService(db).CreateTripAsync(route.Id, bus.Id, null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        var trip = (await db.Trips.FindAsync(tripId))!;
        trip.Status = status;
        await db.SaveChangesAsync();
        var rows = (await service.GetRouteAsync(route.Id))!.Stops;
        foreach (var action in new[] { RouteStopAction.Remove, RouteStopAction.MoveDown, RouteStopAction.MoveUp })
        {
            var result = await service.ChangeRouteStopAsync(route.Id, action, rows[0].Id);
            Assert.False(result.Success);
            Assert.Contains("used by a trip", result.Error);
        }
        Assert.False((await service.DeleteRouteAsync(route.Id)).Success);
        Assert.False((await service.DeleteBusAsync(bus.Id)).Success);
        Assert.False((await service.DeleteStopAsync(rows[0].StopId)).Success);
        AssertOrder((await service.GetRouteAsync(route.Id))!.Stops, "A", "B");
        var extra = await service.SaveStopAsync(null, new() { Name = "C", Latitude = 0, Longitude = 2 });
        Assert.True((await service.ChangeRouteStopAsync(route.Id, RouteStopAction.Add, extra.Id)).Success);
        AssertOrder((await service.GetRouteAsync(route.Id))!.Stops, "A", "B", "C");
        Assert.Equal(2, await db.TripStops.CountAsync(s => s.TripId == tripId));
    }

    [Fact]
    public async Task Failed_second_save_rolls_back_temporary_sequences()
    {
        await using var database = await TestDb.CreateAsync();
        var route = new BusRoute { Name = "Atomic", RouteStops = new List<RouteStop> {
            new() { SequenceOrder = 1, Stop = new Stop { Name = "A" } }, new() { SequenceOrder = 2, Stop = new Stop { Name = "B" } } } };
        database.Context.Add(route);
        await database.Context.SaveChangesAsync();
        await using var failing = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>()
            .UseSqlite(database.Context.Database.GetDbConnection()).AddInterceptors(new FailSave(2)).Options);
        var result = await new AdminMasterDataService(failing).ChangeRouteStopAsync(route.Id, RouteStopAction.MoveDown, route.RouteStops.First().Id);
        Assert.False(result.Success);
        database.Context.ChangeTracker.Clear();
        AssertOrder((await new AdminMasterDataService(database.Context).GetRouteAsync(route.Id))!.Stops, "A", "B");
    }

    [Fact]
    public async Task Bus_and_stop_CRUD_validate_normalize_and_enforce_registration_uniqueness()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = new AdminMasterDataService(db);
        var created = await service.SaveBusAsync(null, new() { RegistrationNumber = " nd 123 ", Model = " Model ", Capacity = 10 });
        Assert.True(created.Success);
        var bus = await service.GetBusAsync(created.Id);
        Assert.Equal("ND 123", bus!.RegistrationNumber);
        Assert.Equal("Model", bus.Model);
        Assert.False((await service.SaveBusAsync(null, new() { RegistrationNumber = "nd 123", Capacity = 5 })).Success);
        Assert.True((await service.SaveBusAsync(created.Id, new() { RegistrationNumber = "ND 456", Capacity = 12 })).Success);
        db.Buses.Add(new Bus { RegistrationNumber = "ND 456" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.True((await service.DeleteBusAsync(created.Id)).Success);
        Assert.Null(await service.GetBusAsync(created.Id));
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveBusAsync(null, new() { RegistrationNumber = "X", Capacity = 0 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveBusAsync(null, new() { RegistrationNumber = " ", Capacity = 1 }));
        foreach (var coordinates in new[] { (91d, 0d), (-91d, 0d), (0d, 181d), (0d, -181d) })
            await Assert.ThrowsAsync<ValidationException>(() => service.SaveStopAsync(null, new() { Name = "Invalid", Latitude = coordinates.Item1, Longitude = coordinates.Item2 }));
        var stop = await service.SaveStopAsync(null, new() { Name = " Boundary ", Latitude = -90, Longitude = 180 });
        Assert.Contains("mlat=-90&mlon=180", (await service.GetStopAsync(stop.Id))!.LocationUrl);
        Assert.True((await service.SaveStopAsync(stop.Id, new() { Name = "Edited", Latitude = 90, Longitude = -180 })).Success);
        Assert.True((await service.DeleteStopAsync(stop.Id)).Success);
        Assert.Null(await service.GetStopAsync(stop.Id));
    }

    [Theory]
    [InlineData("bus")]
    [InlineData("stop")]
    [InlineData("route")]
    public async Task Delete_database_failure_returns_a_friendly_message(string kind)
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var bus = new Bus { RegistrationNumber = "DELETE" };
        var stop = new Stop { Name = "Delete" };
        var route = new BusRoute { Name = "Delete" };
        db.AddRange(bus, stop, route);
        await db.SaveChangesAsync();
        await using var failing = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>()
            .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(new FailSave(1)).Options);
        var service = new AdminMasterDataService(failing);
        var result = kind switch {
            "bus" => await service.DeleteBusAsync(bus.Id),
            "stop" => await service.DeleteStopAsync(stop.Id),
            _ => await service.DeleteRouteAsync(route.Id) };
        Assert.False(result.Success);
        Assert.Contains("cannot be deleted", result.Error);
    }

    private static void AssertOrder(List<AdminRouteStopDto> stops, params string[] names)
    {
        Assert.Equal(names, stops.Select(s => s.Name));
        Assert.Equal(Enumerable.Range(1, stops.Count), stops.Select(s => s.SequenceOrder));
        Assert.Equal(stops.Count, stops.Select(s => s.SequenceOrder).Distinct().Count());
    }

    private sealed class FailSave(int failAt) : SaveChangesInterceptor
    {
        private int _count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++_count == failAt) throw new DbUpdateException("Simulated constraint race");
            return ValueTask.FromResult(result);
        }
    }
}
