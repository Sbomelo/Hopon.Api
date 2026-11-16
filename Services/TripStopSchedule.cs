using Hopon.Api.Models;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services;

public static class TripStopSchedule
{
    public static List<TripStop> Create(IReadOnlyList<RouteStop> routeStops, DateTime departure, DateTime arrival)
    {
        var ordered = routeStops.OrderBy(s => s.SequenceOrder).ToList();
        var arrivals = CalculateArrivals(ordered.Select(s => s.Stop).ToList(), departure, arrival);
        return ordered.Select((stop, index) => new TripStop
        {
            StopId = stop.StopId,
            SequenceOrder = stop.SequenceOrder,
            Status = TripStopStatus.Pending,
            ScheduledArrival = arrivals[index],
            ScheduledDeparture = GetDeparture(arrivals, index)
        }).ToList();
    }

    public static void RescalePending(IReadOnlyList<TripStop> tripStops, DateTime departure, DateTime arrival)
    {
        var ordered = tripStops.OrderBy(s => s.SequenceOrder).ToList();
        // Legacy trips without stops are left intact; Task 12 supplies reproducible demo data.
        if (ordered.Count == 0)
            return;

        var arrivals = CalculateArrivals(ordered.Select(s => s.Stop).ToList(), departure, arrival);
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Status != TripStopStatus.Pending)
                continue;

            ordered[index].ScheduledArrival = arrivals[index];
            ordered[index].ScheduledDeparture = GetDeparture(arrivals, index);
        }
    }

    private static List<DateTime> CalculateArrivals(IReadOnlyList<Stop> stops, DateTime departure, DateTime arrival)
    {
        if (arrival <= departure)
            throw new InvalidOperationException("Arrival must be after departure.");
        if (stops.Count < 2)
            throw new InvalidOperationException("The route must have at least two stops before creating a trip.");

        var cumulativeDistances = new double[stops.Count];
        for (var index = 1; index < stops.Count; index++)
        {
            var previous = stops[index - 1];
            var current = stops[index];
            var distance = GeoMath.HaversineKilometers(previous.Latitude, previous.Longitude,
                current.Latitude, current.Longitude);
            if (!double.IsFinite(distance) || distance <= 0)
                throw new InvalidOperationException("Consecutive route stops must have distinct, valid coordinates.");
            cumulativeDistances[index] = cumulativeDistances[index - 1] + distance;
        }

        var duration = arrival.Ticks - departure.Ticks;
        var arrivals = cumulativeDistances.Select(distance => departure.AddTicks(
            (long)(duration * (distance / cumulativeDistances[^1])))).ToList();
        arrivals[0] = departure;
        arrivals[^1] = arrival;
        if (arrivals.Zip(arrivals.Skip(1)).Any(pair => pair.First >= pair.Second))
            throw new InvalidOperationException("The schedule is too short to give every stop a later arrival.");
        return arrivals;
    }

    private static DateTime? GetDeparture(IReadOnlyList<DateTime> arrivals, int index)
    {
        if (index == arrivals.Count - 1)
            return null;
        if (index == 0)
            return arrivals[0];
        return arrivals[index + 1] - arrivals[index] < TimeSpan.FromMinutes(15)
            ? arrivals[index + 1] : arrivals[index].AddMinutes(15);
    }
}
