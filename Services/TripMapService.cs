using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models;
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

        public async Task<TripMapDataDto?> GetMapDataAsync(int tripId, int? destinationTripStopId = null)
        {
            var trip = await _db.Trips
                        .Include(t => t.BusRoute)
                        .Include(t => t.TripStops.OrderBy(ts => ts.SequenceOrder))
                           .ThenInclude(ts => ts.Stop)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return null;

            var destination = trip.TripStops.FirstOrDefault(stop => stop.Id == destinationTripStopId);

            //Last GPS the driver sent
            var fixes = await _db.LocationUpdates
                        .Where(lu => lu.TripId == tripId)
                        .OrderByDescending(lu => lu.RecordedAt)
                        .ThenByDescending(lu => lu.Id).Take(10)
                        .Select(lu => new MapLocationDto { Latitude = lu.Latitude, Longitude = lu.Longitude, RecordedAt = lu.RecordedAt })
                        .ToListAsync();

            return new TripMapDataDto
            {
                TripId = trip.Id,
                RouteName = trip.BusRoute.Name,
                Status = trip.Status.ToString(),
                DestinationStopName = destination?.Stop.Name,
                DestinationEstimatedArrival = destination is null ? null
                    : EtaCalculator.CalculateStopEta(trip, destination).EstimatedArrival,
                Stops = trip.TripStops.Select(ts => new MapStopDto
                {
                    SequenceOrder = ts.SequenceOrder,
                    Name = ts.Stop.Name,
                    Latitude = ts.Stop.Latitude,
                    Longitude = ts.Stop.Longitude,
                    Status = ts.Status.ToString()
                }).ToList(),

                RoadCoordinates = DemoRoad.Matches(trip.BusRoute.Name, trip.TripStops.Select(s => s.Stop.Name))
                    ? DemoRoad.N3.Coordinates : null,
                LastKnownLocation = fixes.FirstOrDefault(),
                RecentLocations = fixes.AsEnumerable().Reverse().ToList()
            };
        }
    }
}
