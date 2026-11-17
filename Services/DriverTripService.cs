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
        private readonly INotificationService _notificationService;

        public DriverTripService(HoponDbContext db, ILogger<DriverTripService> logger, IHubContext<TripHub> hubContext, INotificationService notificationService)
        {
            _db = db;
            _logger = logger;
            _hubContext = hubContext;
            _notificationService = notificationService;
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

            await _notificationService.NotifyTripDelayedAsync(trip, reason);

            await BroadcastStatusAsync(trip, reason: reason);

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ResumeFromDelayAsync(Trip trip)
        {
            if (trip.Status != TripStatus.Delayed)
                return (false, $"Trip is not currently delayed. Current status: {trip.Status}.");

            trip.Status = TripStatus.InProgress;
            await _db.SaveChangesAsync();

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

            await _notificationService.NotifyTripCompletedAsync(trip);

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

        public async Task<(bool Success, string? Error)> MarkStopArrivedAsync(Trip trip, int tripStopId)
        {
            var ts = await _db.TripStops
                .Include(x => x.Stop)
                .FirstOrDefaultAsync(x => x.Id == tripStopId && x.TripId == trip.Id);

            if (ts is null) return (false, "Stop not found.");
            if (ts.Status != TripStopStatus.Pending)
                return (false, $"Stop is already {ts.Status}.");

            ts.Status = TripStopStatus.Arrived;
            ts.ActualArrival = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _notificationService.NotifyStopArrivedAsync(ts);

            await _hubContext.Clients.Group(TripHub.GroupName(ts.TripId))
                .SendAsync("ReceiveStopUpdate", new
                {
                    tripStopId = ts.Id,
                    stopName = ts.Stop.Name,
                    status = ts.Status.ToString(),
                    actualArrival = ts.ActualArrival
                });

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> MarkStopDepartedAsync(Trip trip, int tripStopId)
        {
            var ts = await _db.TripStops
                .Include(x => x.Stop)
                .FirstOrDefaultAsync(x => x.Id == tripStopId && x.TripId == trip.Id);

            if (ts is null) return (false, "Stop not found.");
            if (ts.Status != TripStopStatus.Arrived)
                return (false, "Mark the stop as Arrived before departing.");

            ts.Status = TripStopStatus.Departed;
            ts.ActualDeparture = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hubContext.Clients.Group(TripHub.GroupName(ts.TripId))
                .SendAsync("ReceiveStopUpdate", new
                {
                    tripStopId = ts.Id,
                    stopName = ts.Stop.Name,
                    status = ts.Status.ToString(),
                    actualDeparture = ts.ActualDeparture
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
