using System.Net;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class DemoModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 1, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task Seeding_twice_keeps_ids_counts_and_passenger_history()
    {
        await using var store = await TestDb.CreateAsync();
        var db = store.Context;
        var seed = new DemoSeeder(db, new FakeTimeProvider(Now));
        await seed.SeedAsync();
        async Task<int[]> Counts() => [await db.Trips.CountAsync(), await db.Tickets.CountAsync(),
            await db.TripStops.CountAsync(), await db.BoardingLogs.CountAsync(), await db.LocationUpdates.CountAsync(),
            await db.Users.CountAsync(), await db.Drivers.CountAsync(), await db.Buses.CountAsync(),
            await db.BusRoutes.CountAsync(), await db.Stops.CountAsync(), await db.RouteStops.CountAsync(), await db.EmergencyContacts.CountAsync()];
        var counts = await Counts();
        var ids = await db.Tickets.OrderBy(k => k.Id).Select(t => t.Id).ToArrayAsync();
        db.ChangeTracker.Clear();
        await seed.SeedAsync();
        Assert.Equal(counts, await Counts());
        Assert.Equal(ids, await db.Tickets.OrderBy(k => k.Id).Select(t => t.Id).ToArrayAsync());
        var live = await db.Trips.SingleAsync(t => t.Status == TripStatus.InProgress);
        Assert.Equal(2, live.DriverId);
        Assert.Equal(Now.UtcDateTime.AddHours(-7.5), live.ActualDeparture);
        Assert.Equal(TimeSpan.FromMinutes(10), live.ActualDeparture - live.ScheduledDeparture);
        var stops = await db.TripStops.Where(s => s.TripId == live.Id).OrderBy(s => s.SequenceOrder).ToListAsync();
        Assert.All(stops.Take(4), s => Assert.Equal(TripStopStatus.Departed, s.Status));
        Assert.Equal(TripStopStatus.Pending, stops[^1].Status);
        Assert.Equal(Now.UtcDateTime.AddHours(2), EtaCalculator.CalculateStopEta(live, stops[^1]).EstimatedArrival);
        Assert.Equal(3, await db.Tickets.CountAsync(t => t.DestinationTripStopId == stops[4].Id));
        Assert.Single(await db.Tickets.Where(t => t.OriginTripStopId == stops[1].Id).ToListAsync());
        Assert.DoesNotContain(await db.Tickets.Select(t => t.User.PhoneNumber).ToListAsync(),
            n => n != "+27600000001" && n != "+27600000002" && n != "+27600000003");
        Assert.Equal(6, await db.BoardingLogs.CountAsync());
        Assert.Equal(Now.UtcDateTime.AddDays(-1), (await db.Trips.SingleAsync(t => t.Status == TripStatus.Completed)).ActualArrival);
        Assert.Equal("+27600000004", (await db.EmergencyContacts.SingleAsync()).PhoneNumber);
        var map = await new TripMapService(db).GetMapDataAsync(live.Id);
        Assert.Equal(5, map!.RecentLocations.Count);
        Assert.Equal(DemoRoad.Villiers.Longitude, map.LastKnownLocation!.Longitude, 6);
        Assert.InRange(map.RoadCoordinates!.Length, 150, 250);
    }

    [Fact]
    public async Task Reset_replaces_only_seeded_trips_even_when_route_bus_and_passenger_are_shared()
    {
        await using var store = await TestDb.CreateAsync();
        var db = store.Context;
        var seed = new DemoSeeder(db, new FakeTimeProvider(Now));
        await seed.SeedAsync();
        var original = await db.Trips.SingleAsync(t => t.Status == TripStatus.InProgress);
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == "+27600000001");
        var other = new Trip { BusId = original.BusId, BusRouteId = original.BusRouteId };
        var ticket = new Ticket { Trip = other, UserId = user.Id, TicketReference = "OPERATOR-1" };
        ticket.BoardingLogs.Add(new BoardingLog());
        other.LocationUpdates.Add(new LocationUpdate());
        db.Tickets.Add(ticket);
        db.NotificationLogs.AddRange(new NotificationLog { Trip = other, UserId = user.Id }, new NotificationLog { Trip = original, UserId = user.Id });
        db.AccessAuditLogs.AddRange(new AccessAuditLog { TripId = original.Id, UserId = user.Id },
            new AccessAuditLog { UserId = user.Id }, new AccessAuditLog { UserId = 1 });
        await db.SaveChangesAsync();
        db.AccessAuditLogs.Add(new AccessAuditLog { TripId = other.Id, UserId = user.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await seed.SeedAsync(reset: true);
        Assert.False(await db.Trips.AnyAsync(t => t.Id == original.Id));
        Assert.True(await db.BoardingLogs.AnyAsync(b => b.TicketId == ticket.Id));
        Assert.True(await db.LocationUpdates.AnyAsync(l => l.TripId == other.Id));
        Assert.Equal(other.Id, (await db.NotificationLogs.SingleAsync()).TripId);
        Assert.Equal(2, await db.AccessAuditLogs.CountAsync());
        Assert.True(await db.AccessAuditLogs.AnyAsync(a => a.TripId == other.Id));
        Assert.True(await db.AccessAuditLogs.AnyAsync(a => a.UserId == 1 && a.TripId == null));
        Assert.Equal(3, await db.Trips.CountAsync());
        Assert.Equal(7, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Reset_rolls_back_if_an_operator_issued_a_ticket_on_the_demo_trip()
    {
        await using var store = await TestDb.CreateAsync();
        var db = store.Context;
        var seed = new DemoSeeder(db, TimeProvider.System);
        await seed.SeedAsync();
        var past = await db.Trips.SingleAsync(t => t.Status == TripStatus.Completed);
        db.Tickets.Add(new Ticket { Trip = past, UserId = 1, TicketReference = "KEEP" });
        await db.SaveChangesAsync();
        var tripIds = await db.Trips.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => seed.SeedAsync(reset: true));
        db.ChangeTracker.Clear();
        Assert.Equal(tripIds, await db.Trips.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync());
        Assert.Equal(7, await db.Tickets.CountAsync());
    }

    [Fact]
    public void Interpolation_crosses_vertices_and_clamps_at_both_ends()
    {
        var road = DemoRoad.N3;
        var start = road.DistanceAlong(DemoRoad.Villiers);
        Assert.InRange(road.DistanceAlong(road.At(start + 10)) - start, 9.99, 10.01);
        Assert.Equal(0, road.DistanceAlong(road.At(-1)), 5);
        Assert.Equal(road.Length, road.DistanceAlong(road.At(road.Length + 100)), 5);
    }

    [Fact]
    public async Task Simulator_uses_latest_broadcast_fix_throttle_and_lifecycle_transitions()
    {
        using var staff = new StaffApplication();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var app = staff.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
        var sim = scope.ServiceProvider.GetRequiredService<SimulatorService>();
        var demo = (await sim.ChoicesAsync()).First();
        Assert.True(demo.IsDemo);
        var id = demo.Id;
        Assert.True((await sim.ActAsync(101, "start")).Success);
        Assert.False((await sim.ActAsync(id, "start")).Success);
        Assert.False((await sim.ActAsync(id, "depart")).Success);
        Assert.True((await sim.ActAsync(id, "move")).Success);
        Assert.False((await sim.ActAsync(id, "jump")).Success);
        var road = DemoRoad.N3;
        var moved = road.DistanceAlong((await sim.SnapshotAsync(id))!.Position!.Value);
        Assert.Equal(10, moved - road.DistanceAlong(DemoRoad.Villiers), 3);
        for (var tick = 1; tick <= 3; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True((await sim.ActAsync(id, "auto")).Success);
            var km = road.DistanceAlong((await sim.SnapshotAsync(id))!.Position!.Value);
            Assert.Equal(tick * 110 * 3 / 3600.0, km - moved, 4);
        }
        Assert.Equal(6, await db.LocationUpdates.CountAsync(l => l.TripId == id));
        Assert.False((await sim.ActAsync(id, "delay", " ")).Success);
        Assert.True((await sim.ActAsync(id, "delay", "Traffic at Villiers")).Success);
        Assert.Equal(3, await db.NotificationLogs.CountAsync(n => n.TripId == id && n.Type == NotificationType.Delay));
        Assert.False((await sim.ActAsync(id, "move")).Success);
        Assert.True((await sim.ActAsync(id, "resume")).Success);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True((await sim.ActAsync(id, "jump")).Success);
        Assert.True((await sim.SnapshotAsync(id))!.AtStop);
        Assert.True((await sim.ActAsync(id, "depart")).Success);
        Assert.True((await sim.ActAsync(id, "complete")).Success);
        Assert.False((await sim.ActAsync(id, "auto")).Success);
        Assert.Equal(TripStatus.Completed, (await sim.SnapshotAsync(id))!.Status);
        Assert.DoesNotContain(await sim.ChoicesAsync(), t => t.Id == id);
    }

    [Fact]
    public async Task Simulator_requires_admin_cookie_and_antiforgery_and_returns_working_controls()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Simulator")).StatusCode);
        var login = await client.GetStringAsync("/Staff/Login");
        string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync("/Staff/Login", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = Token(login), ["Input.Role"] = "Admin", ["Input.Username"] = "admin-test",
            ["Input.Password"] = StaffApplication.Password }));
        const string post = "/Admin/Simulator?handler=Action";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(post, new FormUrlEncodedContent([]))).StatusCode);
        var page = await client.GetStringAsync("/Admin/Simulator?tripId=101");
        Assert.Contains("Start trip", page);
        client.DefaultRequestHeaders.Add("X-Simulator-Panel", "1");
        var response = await client.PostAsync(post, new FormUrlEncodedContent(new Dictionary<string, string> {
            ["tripId"] = "101", ["command"] = "start", ["__RequestVerificationToken"] = Token(page) }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("InProgress", await response.Content.ReadAsStringAsync());
    }
}
