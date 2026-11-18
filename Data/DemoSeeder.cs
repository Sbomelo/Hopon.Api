using System.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Data;

public sealed class DemoSeeder(HoponDbContext db, TimeProvider clock)
{
    public const string Registration = "DEMO 001 GP";
    public const string LiveTicket = "HOPON-DEMO-LIVE-1";
    private static readonly string[] Phones = ["+27600000001", "+27600000002", "+27600000003"];

    public async Task SeedAsync(bool reset = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var driver = await db.Drivers.SingleAsync(d => d.Id == 2 && d.Username == "driver1");
        var passengers = new List<User>();
        for (var i = 0; i < Phones.Length; i++)
        {
            var phone = Phones[i];
            var passenger = await db.Users.Include(u => u.EmergencyContact).SingleOrDefaultAsync(u => u.PhoneNumber == phone);
            var name = $"Demo Passenger {i + 1}";
            if (passenger is null)
            {
                passenger = new User { FullName = name, PhoneNumber = phone };
                db.Users.Add(passenger);
            }
            else if (passenger.FullName != name)
                throw new InvalidOperationException("A reserved demo phone belongs to another passenger. Demo seeding stopped.");
            passengers.Add(passenger);
        }
        if (passengers[0].EmergencyContact is null || reset)
        {
            var contact = passengers[0].EmergencyContact ??= new EmergencyContact();
            contact.Name = "Demo Contact";
            contact.PhoneNumber = "+27600000004";
        }

        var bus = await db.Buses.SingleOrDefaultAsync(b => b.RegistrationNumber == Registration)
            ?? new Bus { RegistrationNumber = Registration, Capacity = 60, Model = "Demo intercity coach" };
        var route = await db.BusRoutes.Include(r => r.RouteStops).ThenInclude(rs => rs.Stop)
            .SingleOrDefaultAsync(r => r.Name == DemoRoad.RouteName) ?? new BusRoute { Name = DemoRoad.RouteName };
        var stops = await GetRouteStopsAsync(route);
        if (bus.Id == 0) db.Buses.Add(bus);
        if (route.Id == 0) db.BusRoutes.Add(route);
        await db.SaveChangesAsync();

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var history in new[] { false, true })
        {
            var prefix = history ? "HOPON-DEMO-PAST-" : "HOPON-DEMO-LIVE-";
            var references = Enumerable.Range(1, 3).Select(i => prefix + i).ToArray();
            var existing = await db.Trips.SingleOrDefaultAsync(t => t.BusRouteId == route.Id && t.BusId == bus.Id
                && t.Tickets.Any(k => references.Contains(k.TicketReference)));
            if (existing != null)
            {
                if (!reset) continue;
                await RemoveDemoTripAsync(existing, passengers, references);
            }
            // Anchor the completed arrival to yesterday even for a pitch just after midnight.
            var departure = history ? now.AddDays(-1).AddMinutes(-580) : now.AddMinutes(-460);
            var trip = new Trip
            {
                BusRoute = route, Driver = driver, Bus = bus, ScheduledDeparture = departure,
                ActualDeparture = departure.AddMinutes(10), ScheduledArrival = departure.AddMinutes(570),
                ActualArrival = history ? departure.AddMinutes(580) : null,
                Status = history ? TripStatus.Completed : TripStatus.InProgress,
                CreatedAt = departure.AddDays(-1)
            };
            var tripStops = AddTripStops(trip, stops, history);
            for (var i = 0; i < passengers.Count; i++)
            {
                var origin = tripStops[i == 1 ? 1 : 0];
                var ticket = new Ticket
                {
                    TicketReference = references[i], User = passengers[i], SeatNumber = $"{i + 11}A",
                    OriginTripStop = origin, DestinationTripStop = tripStops[^1], PurchaseDate = trip.CreatedAt
                };
                trip.Tickets.Add(ticket);
                if (history)
                {
                    ticket.BoardingLogs.Add(new BoardingLog { EventType = BoardingEventType.Boarded, Stop = origin.Stop,
                        Timestamp = origin.ActualDeparture!.Value.AddMinutes(-2) });
                    ticket.BoardingLogs.Add(new BoardingLog { EventType = BoardingEventType.Alighted, Stop = stops[^1],
                        Timestamp = trip.ActualArrival!.Value });
                }
            }
            if (!history)
            {
                var villiersKm = DemoRoad.N3.DistanceAlong(DemoRoad.Villiers);
                foreach (var minutesAgo in new[] { 24, 18, 12, 6, 0 })
                {
                    var point = DemoRoad.N3.At(villiersKm - minutesAgo * 1.5);
                    trip.LocationUpdates.Add(new LocationUpdate { Latitude = point.Latitude, Longitude = point.Longitude,
                        RecordedAt = now.AddSeconds(-16).AddMinutes(-minutesAgo) });
                }
            }
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
        }
        if (reset)
        {
            var userIds = passengers.Select(p => p.Id).ToArray();
            await db.AccessAuditLogs.Where(a => a.TripId == null && userIds.Contains(a.UserId)).ExecuteDeleteAsync();
        }
        await transaction.CommitAsync();
    }

    private async Task<List<Stop>> GetRouteStopsAsync(BusRoute route)
    {
        var assigned = route.RouteStops.OrderBy(rs => rs.SequenceOrder).ToList();
        var expected = DemoRoad.Stops;
        if (!assigned.Select(s => s.Stop.Name).SequenceEqual(expected.Take(assigned.Count).Select(s => s.Name)))
            throw new InvalidOperationException("An existing route with the demo name has a different stop order.");
        var stops = new List<Stop>();
        for (var i = 0; i < expected.Length; i++)
        {
            if (i < assigned.Count) { stops.Add(assigned[i].Stop); continue; }
            var (name, point, _, _) = expected[i];
            var stop = await db.Stops.SingleOrDefaultAsync(s => s.Name == name)
                ?? new Stop { Name = name, Latitude = point.Latitude, Longitude = point.Longitude };
            route.RouteStops.Add(new RouteStop { Stop = stop, SequenceOrder = i + 1 });
            stops.Add(stop);
        }
        return stops;
    }

    private static List<TripStop> AddTripStops(Trip trip, List<Stop> stops, bool history)
    {
        var result = new List<TripStop>();
        for (var i = 0; i < stops.Count; i++)
        {
            var (_, _, arrival, departure) = DemoRoad.Stops[i];
            var last = i == stops.Count - 1;
            var scheduled = trip.ScheduledDeparture;
            var stop = new TripStop
            {
                Stop = stops[i], SequenceOrder = i + 1, ScheduledArrival = scheduled.AddMinutes(arrival),
                ScheduledDeparture = departure.HasValue ? scheduled.AddMinutes(departure.Value) : null,
                ActualArrival = history || !last ? scheduled.AddMinutes(arrival + (i == 0 ? 5 : 10)) : null,
                ActualDeparture = departure.HasValue ? scheduled.AddMinutes(departure.Value + 10) : null,
                Status = last ? (history ? TripStopStatus.Arrived : TripStopStatus.Pending) : TripStopStatus.Departed
            };
            trip.TripStops.Add(stop);
            result.Add(stop);
        }
        return result;
    }

    private async Task RemoveDemoTripAsync(Trip trip, List<User> passengers, string[] references)
    {
        var ids = passengers.Select(p => p.Id).ToArray();
        var tickets = db.Tickets.Where(t => t.TripId == trip.Id);
        if (await tickets.AnyAsync(t => !ids.Contains(t.UserId) || !references.Contains(t.TicketReference))
            || await db.NotificationLogs.AnyAsync(n => n.TripId == trip.Id && !ids.Contains(n.UserId))
            || await db.AccessAuditLogs.AnyAsync(a => a.TripId == trip.Id && !ids.Contains(a.UserId)))
            throw new InvalidOperationException("This demo trip contains non-demo passenger records. Reset cancelled.");

        await db.AccessAuditLogs.Where(a => a.TripId == trip.Id).ExecuteDeleteAsync();
        await db.NotificationLogs.Where(n => n.TripId == trip.Id).ExecuteDeleteAsync();
        await tickets.ExecuteDeleteAsync(); // Cascades to boarding logs before the restricted stop references.
        await db.Trips.Where(t => t.Id == trip.Id).ExecuteDeleteAsync(); // Cascades to trip stops and GPS.
    }
}
