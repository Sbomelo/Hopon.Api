using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Hopon.Api.DTOs.RealTime;

namespace Hopon.Api.Services
{
    public class TripLifecycleService : ITripLifecycleService
    {
        private readonly HoponDbContext _db;
        private readonly ILocationThrottle _locationThrottle;
        private readonly ILogger<TripLifecycleService> _logger;
        private readonly IHubContext<TripHub> _hubContext;
        private readonly INotificationService _notificationService;

        public TripLifecycleService(HoponDbContext db, ILogger<TripLifecycleService> logger, IHubContext<TripHub> hubContext, INotificationService notificationService, ILocationThrottle locationThrottle)
        {
            _db = db;
            _locationThrottle = locationThrottle;
            _logger = logger;
            _hubContext = hubContext;
            _notificationService = notificationService;
        }


        public async Task<(bool Success, string? Error)> StartTripAsync(Trip trip)
        {

            if (trip.Status != TripStatus.Scheduled || !TripStatusRules.CanTransition(trip.Status, TripStatus.InProgress))
                return (false, $"Cannot start a trip that is {trip.Status}. Start requires Scheduled; use Resume for a delayed trip.");

            trip.Status = TripStatus.InProgress;
            trip.ActualDeparture = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await BroadcastStatusAsync(trip, reason: null);

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> ReportDelayedAsync(Trip trip, string reason)
        {
            if (!TripStatusRules.CanTransition(trip.Status, TripStatus.Delayed))
                return InvalidTransition(trip, TripStatus.Delayed);

            trip.Status = TripStatus.Delayed;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Trip {TripId} delayed: {Reason}", trip.Id, reason);

            await _notificationService.NotifyTripDelayedAsync(trip, reason);

            await BroadcastStatusAsync(trip, reason: reason);

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ResumeFromDelayAsync(Trip trip)
        {
            if (trip.Status != TripStatus.Delayed || !TripStatusRules.CanTransition(trip.Status, TripStatus.InProgress))
                return (false, $"Cannot resume a trip that is {trip.Status}. Resume requires Delayed.");

            trip.Status = TripStatus.InProgress;
            await _db.SaveChangesAsync();

            await BroadcastStatusAsync(trip, reason: null);
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> CompletedAsync(Trip trip)
        {
            if (!TripStatusRules.CanTransition(trip.Status, TripStatus.Completed))
                return InvalidTransition(trip, TripStatus.Completed);

            trip.Status = TripStatus.Completed;
            trip.ActualArrival = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await _notificationService.NotifyTripCompletedAsync(trip);

            await BroadcastStatusAsync(trip, reason: null);

            return (true, null);
        }
        public async Task<(bool Success, string? Error)> CancelAsync(Trip trip, string? reason)
        {
            if (!TripStatusRules.CanTransition(trip.Status, TripStatus.Cancelled))
                return InvalidTransition(trip, TripStatus.Cancelled);

            trip.Status = TripStatus.Cancelled;
            await _db.SaveChangesAsync();

            await _notificationService.NotifyTripCancelledAsync(trip, reason);
            await BroadcastStatusAsync(trip, reason);
            return (true, null);
        }

        private static (bool Success, string? Error) InvalidTransition(Trip trip, TripStatus target)
            => (false, $"Cannot change trip status from {trip.Status} to {target}.");

        public async Task<LocationRecordResult> RecordLocationAsync(Trip trip, double latitude, double longitude)
        {
            if (!TripStatusRules.IsTrackingActive(trip.Status))
                return new(LocationOutcome.Rejected, $"Cannot record location for a trip that is {trip.Status}");

            return await _locationThrottle.RecordAsync(trip.Id, latitude, longitude,
                () => _db.LocationUpdates.AsNoTracking().Where(x => x.TripId == trip.Id)
                    .OrderByDescending(x => x.RecordedAt).FirstOrDefaultAsync(),
                async fix =>
                {
                    // Page-load map data may lag by 15 seconds; every accepted fix is broadcast live.
                    _db.LocationUpdates.Add(fix);
                    await _db.SaveChangesAsync();
                },
                fix => _hubContext.Clients.Group(TripHub.GroupName(trip.Id)).SendAsync("ReceiveLocationUpdate", new LocationBroadCastDto
                {
                    TripId = trip.Id, Latitude = fix.Latitude, Longitude = fix.Longitude, RecordedAt = fix.RecordedAt
                }));
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
