using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class UtcTimeTests
{
    private static readonly DateTime Departure = new(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Sa_wall_clock_inputs_ignore_machine_timezone_and_round_trip(DateTimeKind kind)
    {
        var local = new DateTime(2026, 11, 20, 18, 0, 0, kind);
        var utc = SaTime.ToUtc(local);
        Assert.Equal(Departure, utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        var roundTrip = SaTime.ToSa(utc);
        Assert.Equal(local.Ticks, roundTrip.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, roundTrip.Kind);
        Assert.Equal(utc, SaTime.ToUtc(roundTrip));
    }

    [Fact]
    public void Sa_conversion_crosses_dates_and_does_not_shift_utc_twice()
    {
        var local = new DateTime(2026, 1, 1, 0, 30, 0);
        var utc = SaTime.ToUtc(local);
        Assert.Equal(new DateTime(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc), utc);
        Assert.Equal(local, SaTime.ToSa(utc));
        Assert.Equal(utc, SaTime.ToUtc(utc));
        Assert.Equal(TimeSpan.FromHours(2), SaTime.Offset);
    }

    [Fact]
    public void Eta_is_ten_minutes_late_when_1800_sast_departure_is_1610_utc()
    {
        var trip = new Trip
        {
            Status = TripStatus.InProgress,
            ScheduledDeparture = SaTime.ToUtc(new DateTime(2026, 11, 20, 18, 0, 0)),
            ScheduledArrival = SaTime.ToUtc(new DateTime(2026, 11, 21, 2, 0, 0)),
            ActualDeparture = Departure.AddMinutes(10)
        };
        var eta = EtaCalculator.CalculateEta(trip);
        Assert.True(eta.IsEstimate);
        Assert.Equal(TimeSpan.FromMinutes(10), eta.EstimatedArrival - trip.ScheduledArrival);
        Assert.Equal(new DateTime(2026, 11, 21, 2, 10, 0), SaTime.ToSa(eta.EstimatedArrival!.Value));
        var stop = new TripStop { ScheduledArrival = Departure.AddHours(4) };
        Assert.Equal(stop.ScheduledArrival.AddMinutes(10), EtaCalculator.CalculateStopEta(trip, stop).EstimatedArrival);
    }

    [Fact]
    public async Task Conventions_read_required_and_nullable_dates_as_utc_without_changing_ticks_or_nulls()
    {
        await using var database = await TestDb.CreateAsync();
        var stored = new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Unspecified);
        var trip = new Trip
        {
            BusRoute = new BusRoute { Name = "UTC test" }, Bus = new Bus { RegistrationNumber = "UTC" },
            ScheduledDeparture = stored, ScheduledArrival = stored.AddHours(8),
            ActualDeparture = stored.AddMinutes(10), ActualArrival = null,
            TripStops = new List<TripStop> { new() { Stop = new Stop { Name = "UTC stop" },
                SequenceOrder = 1, ScheduledArrival = stored, ScheduledDeparture = stored.AddMinutes(15) } }
        };
        database.Context.Trips.Add(trip);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        var loaded = await database.Context.Trips.Include(t => t.TripStops).AsNoTracking().SingleAsync();
        Assert.Equal(stored.Ticks, loaded.ScheduledDeparture.Ticks);
        Assert.Equal(DateTimeKind.Utc, loaded.ScheduledDeparture.Kind);
        Assert.Equal(stored.AddMinutes(10).Ticks, loaded.ActualDeparture!.Value.Ticks);
        Assert.Equal(DateTimeKind.Utc, loaded.ActualDeparture.Value.Kind);
        Assert.Null(loaded.ActualArrival);
        var stop = Assert.Single(loaded.TripStops);
        Assert.Equal(DateTimeKind.Utc, stop.ScheduledArrival.Kind);
        Assert.Equal(DateTimeKind.Utc, stop.ScheduledDeparture!.Value.Kind);
        Assert.Equal(stored.AddMinutes(15).Ticks, stop.ScheduledDeparture.Value.Ticks);
        Assert.Null(stop.ActualArrival);
    }

    [Fact]
    public async Task Admin_create_edit_and_driver_display_use_sast_while_api_dates_end_in_z()
    {
        using var app = new StaffApplication();
        using var admin = app.NewClient();
        string passengerToken;
        int routeId;
        int busId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = (await db.Trips.FindAsync(101))!;
            routeId = trip.BusRouteId;
            busId = trip.BusId;
            trip.ScheduledDeparture = Departure;
            trip.ScheduledArrival = Departure.AddHours(8);
            trip.ActualDeparture = Departure.AddMinutes(10);
            var stop = (await db.TripStops.FindAsync(101))!;
            stop.ScheduledArrival = Departure;
            stop.ActualArrival = Departure.AddMinutes(12);
            stop.Status = TripStopStatus.Arrived;
            db.LocationUpdates.Add(new LocationUpdate { TripId = 101, RecordedAt = Departure.AddMinutes(20) });
            db.RouteStops.AddRange(
                new RouteStop { BusRouteId = routeId, SequenceOrder = 1, Stop = new Stop { Name = "UTC origin", Latitude = 0, Longitude = 0 } },
                new RouteStop { BusRouteId = routeId, SequenceOrder = 2, Stop = new Stop { Name = "UTC destination", Latitude = 0, Longitude = 1 } });
            db.Tickets.Add(new Ticket { UserId = 1, TripId = 101, TicketReference = "UTC-PASSENGER", DestinationTripStopId = 101 });
            await db.SaveChangesAsync();
            passengerToken = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Users.FindAsync(1))!).Token;
        }
        await Login(admin, "Admin", "admin-test");
        var details = await admin.GetStringAsync("/Admin/Trips/Details/101");
        Assert.Contains("20 Nov 2026, 18:00", details);
        Assert.Contains("20 Nov 2026, 18:10", details);
        Assert.Contains("21 Nov 2026, 02:00", details);
        Assert.Contains("20 Nov 2026, 18:00", await admin.GetStringAsync("/Admin/Trips"));
        Assert.Contains("value=\"2026-11-20T18:00", await admin.GetStringAsync("/Admin/Trips/Edit/101"));

        var created = await PostForm(admin, "/Admin/Trips/Create", "/Admin/Trips/Create",
            ("Input.BusRouteId", routeId.ToString()), ("Input.BusId", busId.ToString()),
            ("Input.ScheduledDeparture", "2026-11-20T18:00"), ("Input.ScheduledArrival", "2026-11-21T02:00"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var createdId = int.Parse(created.Headers.Location!.OriginalString.Split('/').Last());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = await db.Trips.Include(t => t.TripStops).SingleAsync(t => t.Id == createdId);
            Assert.Equal(Departure, trip.ScheduledDeparture);
            Assert.Equal(Departure.AddHours(8), trip.ScheduledArrival);
            Assert.Equal(Departure, trip.TripStops.OrderBy(s => s.SequenceOrder).First().ScheduledArrival);
        }
        var editPath = $"/Admin/Trips/Edit/{createdId}";
        Assert.Contains("value=\"2026-11-20T18:00", await admin.GetStringAsync(editPath));
        var edited = await PostForm(admin, editPath, editPath,
            ("Input.BusRouteId", routeId.ToString()), ("Input.BusId", busId.ToString()),
            ("Input.ScheduledDeparture", "2026-11-21T00:30"), ("Input.ScheduledArrival", "2026-11-21T06:30"));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Contains("value=\"2026-11-21T00:30", await admin.GetStringAsync(editPath));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = (await db.Trips.FindAsync(createdId))!;
            Assert.Equal(new DateTime(2026, 11, 20, 22, 30, 0, DateTimeKind.Utc), trip.ScheduledDeparture);
            db.Tickets.Add(new Ticket { TripId = createdId, UserId = 1, TicketReference = "UTC-MIDNIGHT" });
            await db.SaveChangesAsync();
        }

        using var driver = app.NewClient();
        await Login(driver, "Driver", "driver-one");
        var driverHtml = await driver.GetStringAsync("/Driver/Trip");
        Assert.Contains(">18:00</div>", driverHtml);
        Assert.Contains(">02:00</div>", driverHtml);
        Assert.Contains("18:20:00 SAST", driverHtml);
        Assert.Contains("Sch. 18:00", driverHtml);
        Assert.Contains("Arrived 18:12", driverHtml);

        using var passenger = app.NewClient();
        passenger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", passengerToken);
        using var dashboard = JsonDocument.Parse(await passenger.GetStringAsync("/api/trips/my-trips"));
        var trips = dashboard.RootElement.GetProperty("liveTrips").EnumerateArray().ToList();
        var dto = trips.Single(t => t.GetProperty("tripId").GetInt32() == 101);
        foreach (var property in new[] { "tripDate", "scheduledDeparture", "scheduledArrival", "actualDepature", "estimatedArrival", "destinationScheduledArrival", "destinationEstimatedArrival" })
            Assert.EndsWith("Z", dto.GetProperty(property).GetString());
        Assert.Equal(Departure.AddHours(8).AddMinutes(10), dto.GetProperty("estimatedArrival").GetDateTime());
        Assert.Equal("2026-11-20T22:30:00Z", trips.Single(t => t.GetProperty("tripId").GetInt32() == createdId).GetProperty("tripDate").GetString());
        using var history = JsonDocument.Parse(await passenger.GetStringAsync("/api/trips/101/history"));
        Assert.EndsWith("Z", history.RootElement.GetProperty("stops")[0].GetProperty("actualArrival").GetString());
        using var map = JsonDocument.Parse(await passenger.GetStringAsync("/api/trips/101/map-data"));
        Assert.EndsWith("Z", map.RootElement.GetProperty("lastKnownLocation").GetProperty("recordedAt").GetString());
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string role, string username) =>
        PostForm(client, "/Staff/Login", "/Staff/Login", ("Input.Role", role), ("Input.Username", username), ("Input.Password", StaffApplication.Password));

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string url, string formPage, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }
}
