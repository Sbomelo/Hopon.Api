using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class TripServiceTests
{
    [Theory]
    [InlineData(TripStatus.InProgress, 2, true, false, false)]
    [InlineData(TripStatus.InProgress, 1, false, false, false)]
    [InlineData(TripStatus.InProgress, 1, true, true, true)]
    [InlineData(TripStatus.Completed, 1, true, true, false)]
    public async Task Access_checks_ticket_ownership_and_activity_separately_from_tracking(
        TripStatus status, int ticketOwnerId, bool activeTicket, bool hasAccess, bool trackingActive)
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var trip = CreateTrip(status);
        db.Tickets.Add(new Ticket { Trip = trip, UserId = ticketOwnerId, IsActive = activeTicket, TicketReference = "ACCESS" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var access = await new TripAccessService(db).CheckTripAccessAsync(1, trip.Id);

        Assert.Equal(hasAccess, access.HasAccess);
        Assert.Equal(trackingActive, access.IsTrackingActive);
        Assert.Equal(hasAccess ? trip.Id : (int?)null, access.Trip?.Id);
        Assert.Equal(hasAccess ? "ACCESS" : null, access.Ticket?.TicketReference);
    }

    [Fact]
    public async Task Repeated_boarding_keeps_the_original_timestamp_and_log()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var trip = CreateTrip(TripStatus.InProgress);
        var ticket = new Ticket { Trip = trip, UserId = 1, TicketReference = "BOARD" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var clock = new FakeTimeProvider(HoponWebApplicationFactory.TestNow);
        var service = new BoardingService(db, clock);

        var first = await service.ConfirmBoardingAsync(ticket.Id, trip.Id, trip.Status);
        clock.Advance(TimeSpan.FromSeconds(30));
        var repeat = await service.ConfirmBoardingAsync(ticket.Id, trip.Id, trip.Status);

        Assert.True(first.Success, first.Error);
        Assert.True(first.Status!.HasBoarded);
        Assert.False(first.Status.HasAlighted);
        Assert.False(repeat.Success);
        Assert.Contains("already confirmed boarding", repeat.Error);
        Assert.Null(repeat.Status);
        var log = await db.BoardingLogs.AsNoTracking().SingleAsync();
        Assert.Equal(BoardingEventType.Boarded, log.EventType);
        Assert.Equal(HoponWebApplicationFactory.TestNow.UtcDateTime, log.Timestamp);
        Assert.Equal(log.Timestamp, (await service.GetStatusAsync(ticket.Id, trip.Id)).BoardedAt);
    }

    [Theory]
    [InlineData(TripStatus.InProgress, false, "Confirm boarding")]
    [InlineData(TripStatus.Cancelled, true, "This trip has been canceled")]
    [InlineData(TripStatus.Cancelled, false, "This trip has been cancelled")]
    public async Task Rejected_boarding_actions_leave_no_logs(TripStatus status, bool board, string error)
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var trip = CreateTrip(status);
        var ticket = new Ticket { Trip = trip, UserId = 1, TicketReference = "BOARD" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var service = new BoardingService(db, new FakeTimeProvider(HoponWebApplicationFactory.TestNow));

        var result = board
            ? await service.ConfirmBoardingAsync(ticket.Id, trip.Id, trip.Status)
            : await service.ConfirmAlightingAsync(ticket.Id, trip.Id, trip.Status);

        Assert.False(result.Success);
        Assert.Contains(error, result.Error);
        Assert.Null(result.Status);
        Assert.Empty(await db.BoardingLogs.ToListAsync());
    }

    [Theory]
    [InlineData(1, 2, "2B", "This bus is full.")]
    [InlineData(2, 1, "2B", "This passenger already has a ticket for this trip.")]
    [InlineData(2, 2, " 1a ", "Seat 1A is already taken on this trip.")]
    public async Task Issue_ticket_checks_capacity_passenger_and_normalized_seat(
        int capacity, int nextUserId, string nextSeat, string expectedError)
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var trip = CreateTrip(TripStatus.Scheduled);
        trip.Bus.Capacity = capacity;
        var origin = new TripStop { Stop = new Stop { Name = "Origin" }, SequenceOrder = 1 };
        var destination = new TripStop { Stop = new Stop { Name = "Destination" }, SequenceOrder = 2 };
        trip.TripStops.Add(origin);
        trip.TripStops.Add(destination);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        var service = new AdminTripService(db);

        var first = await service.IssueTicketAsync(trip.Id, 1, " 1a ", destination.Id);
        var next = await service.IssueTicketAsync(trip.Id, nextUserId, nextSeat, destination.Id);

        Assert.True(first.Success, first.Error);
        Assert.False(next.Success);
        Assert.Equal(expectedError, next.Error);
        Assert.Null(next.TicketReference);
        var ticket = await db.Tickets.AsNoTracking().SingleAsync();
        Assert.Equal(first.TicketReference, ticket.TicketReference);
        Assert.Equal("1A", ticket.SeatNumber);
        Assert.Equal(origin.Id, ticket.OriginTripStopId);
        Assert.Equal(destination.Id, ticket.DestinationTripStopId);
    }

    private static Trip CreateTrip(TripStatus status) => new()
    {
        Status = status,
        Bus = new Bus { RegistrationNumber = "TEST-TRIP" },
        BusRoute = new BusRoute { Name = "Test route" },
        ScheduledDeparture = HoponWebApplicationFactory.TestNow.UtcDateTime,
        ScheduledArrival = HoponWebApplicationFactory.TestNow.UtcDateTime.AddHours(8)
    };
}
