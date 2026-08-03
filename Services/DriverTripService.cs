using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Principal;

namespace Hopon.Api.Services
{
    public class DriverTripService : IDriverTripService
    {
        private readonly HoponDbContext _db;
        private readonly ILogger<DriverTripService> _logger;
        
        public DriverTripService(HoponDbContext db, ILogger<DriverTripService> logger)
        {
            _db = db;
            _logger = logger;
        }


        public async Task<(bool Success, string? Error)> StartTripAsync(Trip trip)
        {

            if (trip.Status != TripStatus.Scheduled)
                return (false, $"Trip muat be Scheduled to start. Current trip status is {trip.Status}.");

            trip.Status = TripStatus.InProgress;
            trip.ActualDeparture = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> ReportDelayedAsync(Trip trip, string reason)
        {
            if (trip.Status != TripStatus.InProgress && trip.Status != TripStatus.Delayed)
                return (false, $"Trip must be Inprogress to report a delay. Current Trip Status is {trip.Status}.");

            trip.Status = TripStatus.Delayed;

            await _db.SaveChangesAsync();

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> CompletedAsync(Trip trip)
        {
            if (trip.Status != TripStatus.InProgress && trip.Status != TripStatus.Delayed)
                return (false, $"Trip must be Inprogress to mark it complete. Current trip status is {trip.Status}.");

            trip.Status = TripStatus.Completed;
            trip.ActualArrival = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> RecordLocationAsync(Trip trip, double longitude, double latitude)
        {
            if (!TripStatusRules.IsTrackingActive(trip.Status))
                return (false, $"Cannot record location for a trip that is {trip.Status}");

            _db.LocationUpdates.Add(new LocationUpdate {
                TripId = trip.Id,
                Latitude = latitude,
                Longitude = longitude,
                RecordedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return (true, null);
        }

    }
}
