using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Hopon.Api.Services;

public record SimulatorEvent(DateTime At, string Message);
public record SimulatorChoice(int Id, string Route, TripStatus Status, bool IsDemo);
public record SimulatorSnapshot(int Id, TripStatus Status, RoadPoint? Position, string? NextStop,
    bool AtStop, bool HasRoad, IReadOnlyList<SimulatorEvent> Events);

public sealed class SimulatorService(HoponDbContext db, ITripLifecycleService lifecycle,
    ILocationThrottle locations, IMemoryCache memory, TimeProvider clock)
{
    // At demo scale one gate is enough to keep two simulator tabs from advancing the same stop twice.
    private static readonly SemaphoreSlim Gate = new(1);

    public async Task<List<SimulatorChoice>> ChoicesAsync()
    {
        var active = Enum.GetValues<TripStatus>().Where(TripStatusRules.IsTrackingActive).ToArray();
        return await db.Trips.Where(t => active.Contains(t.Status))
            .OrderByDescending(t => t.Tickets.Any(k => k.TicketReference == DemoSeeder.LiveTicket))
            .ThenBy(t => t.ScheduledDeparture)
            .Select(t => new SimulatorChoice(t.Id, t.BusRoute.Name, t.Status,
                t.Tickets.Any(k => k.TicketReference == DemoSeeder.LiveTicket))).ToListAsync();
    }

    private IQueryable<Trip> Trips() => db.Trips.Include(t => t.BusRoute)
        .Include(t => t.TripStops.OrderBy(s => s.SequenceOrder)).ThenInclude(s => s.Stop);

    private static TripStop? Next(Trip trip) => trip.TripStops.OrderBy(s => s.SequenceOrder)
        .FirstOrDefault(s => s.Status is TripStopStatus.Arrived or TripStopStatus.Pending);

    private static bool HasRoad(Trip trip) => DemoRoad.Matches(trip.BusRoute.Name,
        trip.TripStops.OrderBy(s => s.SequenceOrder).Select(s => s.Stop.Name));

    public async Task<SimulatorSnapshot?> SnapshotAsync(int id)
    {
        var trip = await Trips().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
        if (trip is null) return null;
        var fixes = await db.LocationUpdates.AsNoTracking().Where(f => f.TripId == id)
            .OrderByDescending(f => f.RecordedAt).Take(10).ToListAsync();
        var latest = locations.Latest(id);
        if (latest != null && (fixes.Count == 0 || latest.RecordedAt > fixes[0].RecordedAt)) fixes.Insert(0, latest);
        var events = fixes.Select(GpsEvent).ToList();
        foreach (var s in trip.TripStops)
        {
            if (s.ActualArrival is DateTime a) events.Add(new(a, $"Arrived at {s.Stop.Name}"));
            if (s.ActualDeparture is DateTime d) events.Add(new(d, $"Departed {s.Stop.Name}"));
        }
        if (trip.ActualDeparture is DateTime start) events.Add(new(start, "Trip started"));
        if (trip.ActualArrival is DateTime end) events.Add(new(end, "Trip completed"));
        events.AddRange(memory.Get<SimulatorEvent[]>((typeof(SimulatorEvent), id)) ?? []);
        var next = Next(trip);
        return new(id, trip.Status, fixes.FirstOrDefault() is { } fix ? new(fix.Longitude, fix.Latitude) : null,
            next?.Stop.Name, next?.Status == TripStopStatus.Arrived, HasRoad(trip),
            events.Distinct().OrderByDescending(e => e.At).Take(10).ToList());
    }

    private static SimulatorEvent GpsEvent(LocationUpdate fix) =>
        new(fix.RecordedAt, $"GPS · {fix.Latitude:F5}, {fix.Longitude:F5}");

    private void Remember(int id, SimulatorEvent entry)
    {
        var key = (typeof(SimulatorEvent), id);
        memory.Set(key, (memory.Get<SimulatorEvent[]>(key) ?? []).Prepend(entry).Take(10).ToArray(), TimeSpan.FromDays(1));
    }

    public async Task<(bool Success, string? Error)> ActAsync(int id, string command, string? reason = null)
    {
        await Gate.WaitAsync();
        try
        {
            var trip = await Trips().SingleOrDefaultAsync(t => t.Id == id);
            if (trip == null || !TripStatusRules.IsTrackingActive(trip.Status)) return (false, "This trip is no longer active.");
            var next = Next(trip);
            switch (command)
            {
                case "start": return await lifecycle.StartTripAsync(trip);
                case "complete": return await lifecycle.CompletedAsync(trip);
                case "delay":
                case "resume":
                    var message = reason?.Trim();
                    if (command == "delay" && (string.IsNullOrEmpty(message) || message.Length > 200))
                        return (false, "Enter a delay reason of 1 to 200 characters.");
                    var result = command == "delay" ? await lifecycle.ReportDelayedAsync(trip, message!)
                        : await lifecycle.ResumeFromDelayAsync(trip);
                    if (result.Success)
                    {
                        Remember(id, new(clock.GetUtcNow().UtcDateTime, command == "delay" ? $"Delayed · {message}" : "Trip resumed"));
                    }
                    return result;
                case "depart":
                    if (trip.Status != TripStatus.InProgress || next?.Status != TripStopStatus.Arrived)
                        return (false, "Arrive at the stop before departing.");
                    return await lifecycle.MarkStopDepartedAsync(trip, next.Id);
                case "move":
                case "auto":
                case "jump":
                    if (trip.Status != TripStatus.InProgress || next?.Status != TripStopStatus.Pending)
                        return (false, "Movement requires an in-progress trip with a pending stop. Depart an arrived stop first.");
                    var point = new RoadPoint(next.Stop.Longitude, next.Stop.Latitude);
                    if (command != "jump")
                    {
                        if (!HasRoad(trip)) return (false, "Road movement requires the five-stop Durban to Johannesburg route.");
                        var previous = locations.Latest(id) ?? await db.LocationUpdates.AsNoTracking()
                            .Where(f => f.TripId == id).OrderByDescending(f => f.RecordedAt).FirstOrDefaultAsync();
                        var road = DemoRoad.N3;
                        var here = previous == null ? 0 : road.DistanceAlong(new(previous.Longitude, previous.Latitude));
                        var until = road.DistanceAlong(point);
                        if (until <= here + 0.00001) return (false, "At the next stop. Choose Jump to next stop to mark arrival.");
                        var step = command == "move" ? 10 : 110 * 3 / 3600.0;
                        point = road.At(Math.Min(here + step, until));
                    }
                    var recorded = await lifecycle.RecordLocationAsync(trip, point.Latitude, point.Longitude, simulatorJump: command != "auto");
                    if (recorded.Outcome != LocationOutcome.Recorded) return (false, recorded.Error);
                    if (locations.Latest(id) is { } accepted) Remember(id, GpsEvent(accepted));
                    if (command == "jump") return await lifecycle.MarkStopArrivedAsync(trip, next.Id);
                    return (true, null);
                default: return (false, "Unknown simulator action.");
            }
        }
        finally { Gate.Release(); }
    }
}
