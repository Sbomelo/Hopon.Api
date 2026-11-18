using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class PassengerUiTests
{
    [Theory]
    [InlineData(TripStatus.Scheduled)]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.Cancelled)]
    public async Task Inactive_ticket_history_is_reachable_but_tracking_remains_denied(TripStatus status)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var trip = (await db.Trips.FindAsync(101))!;
        trip.Status = status;
        var ticket = new Ticket { UserId = 1, TripId = 101, TicketReference = "VOID-HISTORY", IsActive = false, SeatNumber = "8" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        db.BoardingLogs.Add(new BoardingLog { TicketId = ticket.Id, EventType = BoardingEventType.Boarded, Timestamp = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(1))!).Token);

        var history = await client.GetFromJsonAsync<TripHistoryDto>("/api/trips/101/history");
        Assert.Equal("VOID-HISTORY", history!.TicketReference);
        Assert.Equal("8", history.SeatNumber);
        Assert.True(history.HasBoarded);
        Assert.NotNull(history.BoardedAt);
        Assert.Single(history.Stops);
        var dashboard = await client.GetFromJsonAsync<MyTripsResponseDto>("/api/trips/my-trips");
        Assert.Empty(dashboard!.LiveTrips);
        Assert.Equal(101, Assert.Single(dashboard.PastTrips).TripId);
        foreach (var endpoint in new[] { "map-data", "boarding-status", "access-check" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trips/101/{endpoint}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/trips/101/board", null)).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(2))!).Token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trips/101/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trips/999/history")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Drivers.FindAsync(101))!).Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/trips/101/history")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trips/101/history")).StatusCode);
    }

    [Fact]
    public async Task Map_destination_comes_from_each_authorized_ticket_with_schedule_offset_and_null_legacy_support()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var trip = (await db.Trips.FindAsync(101))!;
        trip.Status = TripStatus.InProgress;
        trip.ScheduledDeparture = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        trip.ActualDeparture = trip.ScheduledDeparture.AddMinutes(10);
        var first = (await db.TripStops.Include(s => s.Stop).SingleAsync(s => s.Id == 101));
        first.ScheduledArrival = trip.ScheduledDeparture.AddHours(2);
        var destination = new TripStop { TripId = 101, SequenceOrder = 2, ScheduledArrival = trip.ScheduledDeparture.AddHours(4), Stop = new Stop { Name = "Final test destination" } };
        db.TripStops.Add(destination);
        await db.SaveChangesAsync();
        db.Tickets.AddRange(
            new Ticket { UserId = 1, TripId = 101, TicketReference = "STOP-ONE", DestinationTripStopId = first.Id },
            new Ticket { UserId = 2, TripId = 101, TicketReference = "STOP-TWO", DestinationTripStopId = destination.Id },
            new Ticket { UserId = 3, TripId = 101, TicketReference = "LEGACY-MAP" });
        await db.SaveChangesAsync();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        foreach (var id in new[] { 1, 2, 3 })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(id))!).Token);
            var map = await client.GetFromJsonAsync<TripMapDataDto>("/api/trips/101/map-data?destinationTripStopId=102");
            Assert.NotNull(map);
            Assert.Equal(id == 1 ? first.Stop.Name : id == 2 ? destination.Stop.Name : null, map.DestinationStopName);
            Assert.Equal(id == 1 ? first.ScheduledArrival.AddMinutes(10) : id == 2 ? destination.ScheduledArrival.AddMinutes(10) : (DateTime?)null, map.DestinationEstimatedArrival);
        }
        // Even a direct service caller cannot select a destination on a different trip.
        Assert.Null((await new TripMapService(db).GetMapDataAsync(101, 102))!.DestinationStopName);
        destination.Status = TripStopStatus.Arrived;
        destination.ActualArrival = destination.ScheduledArrival.AddMinutes(12);
        await db.SaveChangesAsync();
        Assert.Equal(destination.ActualArrival, (await new TripMapService(db).GetMapDataAsync(101, destination.Id))!.DestinationEstimatedArrival);
    }
}
