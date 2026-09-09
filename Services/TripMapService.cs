using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class TripMapService : ITripMapService
    {
        private readonly HoponDbContext _db;

        public TripMapService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<TripMapDataDto?> GetMapDataAsync(int tripId)
        {
            var trip = await _db.Trips
                        .Include(t => t.BusRoute)
                        .Include(t => t.TripStops.OrderBy(ts => ts.SequenceOrder))
                           .ThenInclude(ts => ts.Stop)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return null;

            //Last GPS the driver sent
            var lastLocation = await _db.LocationUpdates
                        .Where(lu => lu.TripId == tripId)
                        .OrderByDescending(lu => lu.RecordedAt)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

            return new TripMapDataDto
            {
                TripId = trip.Id,
                RouteName = trip.BusRoute.Name,
                Status = trip.Status.ToString(),
                Stops = trip.TripStops.Select(ts => new MapStopDto
                {
                    SequenceOrder = ts.SequenceOrder,
                    Name = ts.Stop.Name,
                    Latitude = ts.Stop.Latitude,
                    Longitude = ts.Stop.Longitude,
                    Status = ts.Status.ToString()
                }).ToList(),

                LastKnownLocation = lastLocation is null? null : new MapLocationDto { 
                    Latitude = lastLocation.Latitude,
                    Longitude = lastLocation.Longitude,
                    RecordedAt= lastLocation.RecordedAt
                }

            };
        }
    }
}
