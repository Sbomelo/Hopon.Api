using System.Collections.Concurrent;
using Hopon.Api.Models;
using Hopon.Api.Services.Interfaces;

namespace Hopon.Api.Services;

public sealed class LocationThrottle(TimeProvider clock) : ILocationThrottle
{
    private readonly ConcurrentDictionary<int, DateTime> _lastAcceptedUtc = new();
    private readonly ConcurrentDictionary<int, LocationUpdate> _previous = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastPersistedUtc = new();
    // Striped locks bound lock memory and serialize validation/persistence per trip.
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public LocationUpdate? Latest(int tripId) => _previous.GetValueOrDefault(tripId);

    public async Task<LocationRecordResult> RecordAsync(int tripId, double latitude, double longitude,
        Func<Task<LocationUpdate?>> loadPrevious, Func<LocationUpdate, Task> persistFix, Func<LocationUpdate, Task> broadcast,
        bool simulatorJump = false)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
            return new(LocationOutcome.Rejected, "Please provide valid GPS coordinates.");
        var gate = _gates[(int)((uint)tripId % (uint)_gates.Length)];
        await gate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (_lastAcceptedUtc.TryGetValue(tripId, out var last) && now - last < TimeSpan.FromSeconds(3))
                return new(LocationOutcome.Throttled, "Location shared recently. Please wait a few seconds.");
            if (!_previous.TryGetValue(tripId, out var previous))
            {
                previous = await loadPrevious();
                if (previous != null)
                {
                    _lastPersistedUtc[tripId] = previous.RecordedAt;
                    if (now - previous.RecordedAt < TimeSpan.FromSeconds(3))
                        return new(LocationOutcome.Throttled, "Location shared recently. Please wait a few seconds.");
                }
            }
            // A deliberate admin fast-forward compresses journey time. Rate and write limits still apply.
            if (previous != null && !simulatorJump)
            {
                var hours = (now - previous.RecordedAt).TotalHours;
                if (hours <= 0 || DistanceKm(previous.Latitude, previous.Longitude, latitude, longitude) / hours > 250)
                    return new(LocationOutcome.Rejected, "GPS jumped too far. Please wait for a more accurate location.");
            }
            var fix = new LocationUpdate { TripId = tripId, Latitude = latitude, Longitude = longitude, RecordedAt = now };
            var persist = !_lastPersistedUtc.TryGetValue(tripId, out var saved) || now - saved >= TimeSpan.FromSeconds(15);
            if (persist)
            {
                await persistFix(fix);
                _lastPersistedUtc[tripId] = now;
            }
            _previous[tripId] = fix;
            _lastAcceptedUtc[tripId] = now;
            await broadcast(fix);
            return new(LocationOutcome.Recorded);
        }
        finally { gate.Release(); }
    }

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * radians / 2), 2) +
            Math.Cos(lat1 * radians) * Math.Cos(lat2 * radians) * Math.Pow(Math.Sin((lng2 - lng1) * radians / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
