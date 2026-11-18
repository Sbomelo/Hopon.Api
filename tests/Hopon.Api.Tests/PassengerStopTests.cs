using Hopon.Api.Controllers;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Pages.Admin.Trips;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Hopon.Api.Tests;

public class PassengerStopTests
{
    private static readonly DateTime Departure = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Admin_form_loads_stops_validates_and_renders_legacy_destination_as_a_dash()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        int destinationId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Hopon.Api.Data.HoponDbContext>();
            var trip = (await db.Trips.FindAsync(101))!;
            var destination = new TripStop { TripId = trip.Id, SequenceOrder = 2,
                ScheduledArrival = trip.ScheduledArrival, Stop = new Stop { Name = "Destination test stop" } };
            db.TripStops.Add(destination);
            db.Tickets.Add(new Ticket { TripId = trip.Id, UserId = 2, TicketReference = "LEGACY-UI" });
            await db.SaveChangesAsync();
            destinationId = destination.Id;
        }
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, "/Staff/Login", "/Staff/Login",
            ("Input.Role", "Admin"), ("Input.Username", "admin-test"), ("Input.Password", StaffApplication.Password))).StatusCode);
        const string path = "/Admin/Trips/Details/101";
        var html = await client.GetStringAsync(path);
        Assert.Contains("Input_DestinationTripStopId", html);
        Assert.Contains("Input_OriginTripStopId", html);
        Assert.Contains("Destination test stop", html);
        var legacyRow = Regex.Match(html, "<tr>\\s*<td>LEGACY-UI</td>.*?</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("<td>—</td>", WebUtility.HtmlDecode(legacyRow));
        var missing = await PostForm(client, path + "?handler=IssueTicket", path, ("Input.UserId", "1"));
        Assert.Contains("Select a destination stop", await missing.Content.ReadAsStringAsync());
        var wrongTrip = await PostForm(client, path + "?handler=IssueTicket", path,
            ("Input.UserId", "1"), ("Input.DestinationTripStopId", "102"));
        Assert.Contains("destination stop belonging to this trip", await wrongTrip.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, path + "?handler=IssueTicket", path,
            ("Input.UserId", "1"), ("Input.DestinationTripStopId", destinationId.ToString()))).StatusCode);
        Assert.Contains("<td>Destination test stop</td>", await client.GetStringAsync(path));

        var rejectedEdit = await PostForm(client, "/Admin/Trips/Edit/101", "/Admin/Trips/Edit/101",
            ("Input.BusRouteId", "102"), ("Input.BusId", "101"),
            ("Input.ScheduledDeparture", "2026-11-20T18:00"), ("Input.ScheduledArrival", "2026-11-21T00:00"));
        Assert.Equal(HttpStatusCode.OK, rejectedEdit.StatusCode);
        Assert.Contains("route cannot be changed because this trip already has tickets", await rejectedEdit.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string url, string formPage, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, 1, 111.1949)]
    [InlineData(-29.8587, 31.0218, -26.2041, 28.0473, 500.5)]
    [InlineData(0, 0, 0, 180, 20015.0868)]
    [InlineData(0, 179, 0, -179, 222.3899)]
    public void Haversine_matches_known_distances_and_is_symmetric(double lat1, double lon1, double lat2, double lon2, double expected)
    {
        var distance = GeoMath.HaversineKilometers(lat1, lon1, lat2, lon2);
        Assert.InRange(distance, expected - 1, expected + 1);
        Assert.Equal(distance, GeoMath.HaversineKilometers(lat2, lon2, lat1, lon1), 8);
    }

    [Fact]
    public async Task Create_generates_ordered_pending_stops_proportional_to_distance()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        var id = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        database.Context.ChangeTracker.Clear();
        var stops = await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).ToListAsync();
        Assert.Equal(new[] { 10, 20, 30 }, stops.Select(s => s.SequenceOrder));
        Assert.All(stops, s => Assert.Equal(TripStopStatus.Pending, s.Status));
        Assert.Equal(Departure, stops[0].ScheduledArrival);
        Assert.Equal(Departure, stops[0].ScheduledDeparture);
        Assert.Equal(Departure.AddHours(2), stops[1].ScheduledArrival);
        Assert.Equal(Departure.AddHours(2).AddMinutes(15), stops[1].ScheduledDeparture);
        Assert.Equal(Departure.AddHours(6), stops[2].ScheduledArrival);
        Assert.Null(stops[2].ScheduledDeparture);
        Assert.True(stops.Zip(stops.Skip(1)).All(p => p.First.ScheduledArrival < p.Second.ScheduledArrival));
    }

    [Fact]
    public async Task Short_schedule_caps_dwell_at_the_next_arrival()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var id = await new AdminTripService(database.Context).CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddMinutes(12));
        var stops = await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).ToListAsync();
        Assert.Equal(Departure.AddMinutes(4), stops[1].ScheduledArrival);
        Assert.Equal(stops[2].ScheduledArrival, stops[1].ScheduledDeparture);
    }

    [Fact]
    public async Task Route_change_replaces_stops_without_tickets_and_preserves_them_when_rejected()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var (otherRoute, _) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        var id = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        Assert.True(await service.UpdateTripAsync(id, otherRoute.Id, bus.Id, null, Departure, Departure.AddHours(9)));
        database.Context.ChangeTracker.Clear();
        var stops = await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).ToListAsync();
        Assert.Equal(otherRoute.RouteStops.Select(s => s.StopId), stops.Select(s => s.StopId));
        Assert.Equal(Departure.AddHours(3), stops[1].ScheduledArrival);
        database.Context.Tickets.Add(new Ticket { TripId = id, UserId = 1, TicketReference = "OLD-INACTIVE", IsActive = false });
        await database.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateTripAsync(id, route.Id, bus.Id, null, Departure, Departure.AddHours(6)));
        Assert.Contains("already has tickets", error.Message);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(otherRoute.Id, (await database.Context.Trips.FindAsync(id))!.BusRouteId);
        Assert.Equal(stops.Select(s => s.Id), await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).Select(s => s.Id).ToListAsync());
    }

    [Fact]
    public async Task Schedule_change_rescales_only_pending_stops_and_keeps_ticket_links()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        var id = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        var stops = await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).ToListAsync();
        stops[0].Status = TripStopStatus.Departed;
        stops[0].ActualArrival = Departure;
        stops[0].ActualDeparture = Departure.AddMinutes(10);
        await database.Context.SaveChangesAsync();
        Assert.True((await service.IssueTicketAsync(id, 1, null, stops[2].Id)).Success);
        Assert.True(await service.UpdateTripAsync(id, route.Id, bus.Id, null, Departure.AddHours(1), Departure.AddHours(10)));
        database.Context.ChangeTracker.Clear();
        var updated = await database.Context.TripStops.Where(s => s.TripId == id).OrderBy(s => s.SequenceOrder).ToListAsync();
        Assert.Equal(stops.Select(s => s.Id), updated.Select(s => s.Id));
        Assert.Equal(Departure, updated[0].ScheduledArrival);
        Assert.Equal(Departure.AddMinutes(10), updated[0].ActualDeparture);
        Assert.Equal(Departure.AddHours(4), updated[1].ScheduledArrival);
        Assert.Equal(Departure.AddHours(10), updated[2].ScheduledArrival);
        Assert.Equal(stops[2].Id, (await database.Context.Tickets.SingleAsync()).DestinationTripStopId);
    }

    [Fact]
    public async Task Issue_rejects_wrong_trip_missing_destination_and_destination_at_or_before_origin()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        var id = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        var otherId = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        var stops = await service.GetTripStopOptionsAsync(id);
        var otherStops = await service.GetTripStopOptionsAsync(otherId);
        Assert.Contains("destination stop belonging", (await service.IssueTicketAsync(id, 1, null, otherStops[2].Id)).Error);
        Assert.Contains("boarding stop belonging", (await service.IssueTicketAsync(id, 1, null, stops[2].Id, otherStops[0].Id)).Error);
        Assert.False((await service.IssueTicketAsync(id, 1, null, 0)).Success);
        Assert.Contains("after the boarding", (await service.IssueTicketAsync(id, 1, null, stops[0].Id, stops[1].Id)).Error);
        Assert.False((await service.IssueTicketAsync(id, 1, null, stops[1].Id, stops[1].Id)).Success);
        Assert.Empty(await database.Context.Tickets.ToListAsync());
        Assert.True((await service.IssueTicketAsync(id, 1, " 14a ", stops[2].Id)).Success);
        var ticket = await database.Context.Tickets.SingleAsync();
        Assert.Equal(stops[0].Id, ticket.OriginTripStopId);
        Assert.Equal(stops[2].Id, ticket.DestinationTripStopId);
        Assert.Equal("14A", ticket.SeatNumber);
        Assert.True((await service.IssueTicketAsync(id, 2, null, stops[2].Id, stops[1].Id)).Success);
    }

    [Theory]
    [InlineData(TripStopStatus.Pending, TripStatus.Scheduled, null, null, 120, true)]
    [InlineData(TripStopStatus.Pending, TripStatus.InProgress, 10, null, 130, true)]
    [InlineData(TripStopStatus.Pending, TripStatus.Delayed, 30, null, 150, true)]
    [InlineData(TripStopStatus.Pending, TripStatus.InProgress, -5, null, 115, true)]
    [InlineData(TripStopStatus.Arrived, TripStatus.InProgress, 10, 125, 125, false)]
    [InlineData(TripStopStatus.Departed, TripStatus.Completed, 10, 125, 125, false)]
    [InlineData(TripStopStatus.Arrived, TripStatus.InProgress, 10, null, null, false)]
    [InlineData(TripStopStatus.Pending, TripStatus.Cancelled, null, null, null, false)]
    [InlineData(TripStopStatus.Pending, TripStatus.Completed, 10, null, null, false)]
    public void Stop_eta_uses_departure_offset_or_actual_arrival(TripStopStatus stopStatus, TripStatus tripStatus,
        int? departureDelay, int? actualArrival, int? expectedMinutes, bool isEstimate)
    {
        var trip = new Trip { Status = tripStatus, ScheduledDeparture = Departure,
            ActualDeparture = departureDelay.HasValue ? Departure.AddMinutes(departureDelay.Value) : null };
        var stop = new TripStop { Status = stopStatus, ScheduledArrival = Departure.AddHours(2),
            ActualArrival = actualArrival.HasValue ? Departure.AddMinutes(actualArrival.Value) : null };
        var result = EtaCalculator.CalculateStopEta(trip, stop);
        Assert.Equal(expectedMinutes.HasValue ? Departure.AddMinutes(expectedMinutes.Value) : (DateTime?)null, result.EstimatedArrival);
        Assert.Equal(isEstimate, result.IsEstimate);
    }

    [Fact]
    public async Task Destination_fields_are_populated_and_legacy_tickets_remain_null()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, bus) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        var id = await service.CreateTripAsync(route.Id, bus.Id, null, Departure, Departure.AddHours(6));
        var stops = await service.GetTripStopOptionsAsync(id);
        Assert.True((await service.IssueTicketAsync(id, 1, null, stops[1].Id)).Success);
        database.Context.Tickets.Add(new Ticket { UserId = 2, TripId = id, TicketReference = "LEGACY" });
        (await database.Context.Trips.FindAsync(id))!.ActualDeparture = Departure.AddMinutes(10);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        foreach (var userId in new[] { 1, 2 })
        {
            var expectedName = userId == 1 ? "Middle" : null;
            DateTime? expectedArrival = userId == 1 ? Departure.AddHours(2) : null;
            var dashboard = Assert.Single((await new TripDashboardService(database.Context).GetMyTripsAsync(userId)).LiveTrips);
            Assert.Equal(expectedName, dashboard.DestinationStopName);
            Assert.Equal(expectedArrival, dashboard.DestinationScheduledArrival);
            Assert.Equal(expectedArrival?.AddMinutes(10), dashboard.DestinationEstimatedArrival);
            Assert.Equal(Departure.AddHours(6).AddMinutes(10), dashboard.EstimatedArrival);
            var history = await new TripHistoryService(database.Context).GetTripHistoryAsync(userId, id);
            Assert.Equal(expectedName, history!.DestinationStopName);
            Assert.Equal(expectedArrival, history.DestinationScheduledArrival);
            var access = await new TripAccessService(database.Context).CheckTripAccessAsync(userId, id);
            var controller = new TripsController(null!, null!, null!, null!, null!)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controller.HttpContext.Items["TripAccessResult"] = access;
            var dto = Assert.IsType<TripAccessResponseDto>(Assert.IsType<OkObjectResult>(controller.CheckAccess(id)).Value);
            Assert.Equal(expectedName, dto.DestinationStopName);
            Assert.Equal(expectedArrival, dto.DestinationScheduledArrival);
        }
        var admin = await service.GetTripDetailAsync(id);
        Assert.Equal("Middle", admin!.Tickets.Single(t => t.TicketReference != "LEGACY").DestinationStopName);
        Assert.Null(admin.Tickets.Single(t => t.TicketReference == "LEGACY").DestinationStopName);
        var page = new DetailsModel(service, null!, null!);
        Assert.IsType<PageResult>(await page.OnGetAsync(id));
        Assert.NotEmpty(page.PassengerItems);
        Assert.Equal(3, page.TripStopItems.Count);
    }

    [Fact]
    public async Task Invalid_route_or_failed_insert_does_not_leave_a_partial_trip()
    {
        await using var database = await TestDb.CreateAsync();
        var (route, _) = await SeedRoute(database);
        var service = new AdminTripService(database.Context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTripAsync(999, 999, null, Departure, Departure.AddHours(6)));
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateTripAsync(route.Id, 999, null, Departure, Departure.AddHours(6)));
        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.Trips.ToListAsync());
        Assert.Empty(await database.Context.TripStops.ToListAsync());
    }

    private static async Task<(BusRoute Route, Bus Bus)> SeedRoute(TestDb database)
    {
        var route = new BusRoute { Name = "Test route", RouteStops = new List<RouteStop>
        {
            new() { SequenceOrder = 10, Stop = new Stop { Name = "First", Latitude = 0, Longitude = 0 } },
            new() { SequenceOrder = 20, Stop = new Stop { Name = "Middle", Latitude = 0, Longitude = 1 } },
            new() { SequenceOrder = 30, Stop = new Stop { Name = "Last", Latitude = 0, Longitude = 3 } }
        } };
        var bus = new Bus { RegistrationNumber = "TEST-" + Guid.NewGuid().ToString("N"), Capacity = 50 };
        database.Context.AddRange(route, bus);
        await database.Context.SaveChangesAsync();
        return (route, bus);
    }
}
