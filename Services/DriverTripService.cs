using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Principal;
using System.Threading.Tasks.Dataflow;
using Hopon.Api.DTOs.RealTime;

namespace Hopon.Api.Services
{
    public class DriverTripService : IDriverTripService
    {
        private readonly HoponDbContext _db;
        private readonly ILogger<DriverTripService> _logger;
        private readonly IHubContext<TripHub> _hubContext;
        
        public DriverTripService(HoponDbContext db, ILogger<DriverTripService> logger, IHubContext<TripHub> hubContext)
        {
            _db = db;
            _logger = logger;
            _hubContext = hubContext;
        }


        public async Task<(bool Success, string? Error)> StartTripAsync(Trip trip)
        {

            if (trip.Status != TripStatus.Scheduled)
                return (false, $"Trip muat be Scheduled to start. Current trip status is {trip.Status}.");

            trip.Status = TripStatus.InProgress;
            trip.ActualDeparture = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await BroadcastStatusAsync(trip, reason: null);

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> ReportDelayedAsync(Trip trip, string reason)
        {
            if (trip.Status != TripStatus.InProgress && trip.Status != TripStatus.Delayed)
                return (false, $"Trip must be Inprogress to report a delay. Current Trip Status is {trip.Status}.");

            trip.Status = TripStatus.Delayed;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Trip {TripId} delayed: {Reason}", trip.Id, reason);

            await BroadcastStatusAsync(trip, reason: null);

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> CompletedAsync(Trip trip)
        {
            if (trip.Status != TripStatus.InProgress && trip.Status != TripStatus.Delayed)
                return (false, $"Trip must be Inprogress to mark it complete. Current trip status is {trip.Status}.");

            trip.Status = TripStatus.Completed;
            trip.ActualArrival = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await BroadcastStatusAsync(trip, reason: null);

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> RecordLocationAsync(Trip trip, double  latitude, double longitude)
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

            await _hubContext.Clients.Group(TripHub.GroupName(trip.Id)).SendAsync("ReceiveLocationUpdate", new LocationBroadCastDto
            {
                TripId = trip.Id,
                Latitude = latitude,
                Longitude = longitude,
                RecordedAt = DateTime.UtcNow
            });

            return (true, null);
        }

        private async Task BroadcastStatusAsync(Trip trip, string? reason)
        {
            await _hubContext.Clients.Group(TripHub.GroupName(trip.Id)).SendAsync("ReceiveTripStatusUpdate", new TripStatusBroadcastDto
            {
                TripId = trip.Id,
                Status = trip.Status.ToString(),
                Reason = reason,
                ActualDeparture = trip.ActualDeparture,
                ActualArrival = trip.ActualArrival

            });
        }

    }
}
