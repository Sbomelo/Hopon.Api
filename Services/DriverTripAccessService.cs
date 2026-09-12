using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class DriverTripAccessService : IDriverTripAccessService
{
    private readonly HoponDbContext _db;

    public DriverTripAccessService(HoponDbContext db)
    {
        _db = db;
    }

    public async Task<DriverTripAccessResult> CheckDriverTripAccessAsync(int driverId, int tripId)
    {
        var trip = await _db.Trips
                             .FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverId);

        return new DriverTripAccessResult
        {
            HasAccess = trip is not null,
            Trip = trip
        };
    }

    public async Task<Trip?> GetActiveTripForDriverAsync(int driverId)
    {
        return await _db.Trips
                         .Include(t => t.BusRoute)
                         .Include(t => t.Bus)
                         .Where(t => t.DriverId == driverId &&
                                    (t.Status == TripStatus.Scheduled ||
                                     t.Status == TripStatus.InProgress ||
                                     t.Status == TripStatus.Delayed))
                         .OrderBy(t => t.ScheduledDeparture)
                         .FirstOrDefaultAsync();
    }

    public async Task<Trip?> GetActiveTripWithStopsForDriverAsync(int driverId)
    {
        return await _db.Trips
            .Include(t => t.BusRoute)
            .Include(t => t.Bus)
            .Include(t => t.TripStops.OrderBy(ts => ts.SequenceOrder))
                .ThenInclude(ts => ts.Stop)
            .Where(t => t.DriverId == driverId &&
               (t.Status == TripStatus.Scheduled ||
                t.Status == TripStatus.InProgress ||
                t.Status == TripStatus.Delayed))
            .OrderBy(t => t.ScheduledDeparture)
            .FirstOrDefaultAsync();
    }

}