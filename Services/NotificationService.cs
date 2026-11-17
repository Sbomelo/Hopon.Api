using Hopon.Api.Data;
using Hopon.Api.DTOs.Notifications;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class NotificationService : INotificationService
    {
        private readonly HoponDbContext _db;
        private readonly PassengerNotificationWriter _writer;
        private readonly TimeProvider _timeProvider;

        public NotificationService(HoponDbContext db, PassengerNotificationWriter writer, TimeProvider timeProvider)
        {
            _db = db;
            _writer = writer;
            _timeProvider = timeProvider;
        }

        public async Task NotifyStopArrivedAsync(TripStop stop)
        {
            var userIds = await _db.Tickets.AsNoTracking()
                .Where(t => t.IsActive && t.TripId == stop.TripId && t.DestinationTripStopId == stop.Id)
                .Select(t => t.UserId).ToListAsync();
            if (userIds.Count == 0)
                return;

            var existing = await _db.NotificationLogs.AsNoTracking()
                .Where(n => n.TripId == stop.TripId && n.Type == NotificationType.Arrival && userIds.Contains(n.UserId))
                .Select(n => n.UserId).ToListAsync();
            var alreadySent = existing.ToHashSet();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var userId in userIds)
            {
                if (alreadySent.Contains(userId))
                    continue;

                await _writer.TryWriteAsync(new NotificationLog
                {
                    UserId = userId, TripId = stop.TripId, Type = NotificationType.Arrival,
                    Channel = NotificationChannel.InApp, Status = NotificationStatus.Sent,
                    Message = NotificationMessages.Arrival(stop.Stop.Name), CreatedAt = now, SentAt = now
                });
            }
        }

        public async Task NotifyTripDelayedAsync(Trip trip, string reason)
        {
            var userIds = await _db.Tickets
                                   .Where(t => t.TripId == trip.Id && t.IsActive)
                                   .Select(t => t.UserId)
                                   .ToListAsync();


            if (userIds.Count == 0)
                return;

            var now = DateTime.UtcNow;

            var message = string.IsNullOrWhiteSpace(reason)
                                 ? "Your bus has been delayed."
                                 : $"Your bus has been delayed: {reason}";

            var logs = userIds.Select(userId => new NotificationLog
            {
                UserId = userId,
                TripId = trip.Id,
                Type = NotificationType.Delay,
                Channel = NotificationChannel.InApp,
                Message = message,
                Status = NotificationStatus.Sent,
                CreatedAt = now,
                SentAt = now
            }).ToList();

            _db.NotificationLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        public async Task<List<NotificationDto>> GetNotificationsForUserAsync(int userId)
        {
            return await _db.NotificationLogs
                             .Where(n => n.UserId == userId)
                             .OrderByDescending(n => n.CreatedAt)
                             .Select(n => new NotificationDto
                             {
                                 Id = n.Id,
                                 TripId = n.TripId,
                                 Type = n.Type.ToString(),
                                 Channel = n.Channel.ToString(),
                                 Message = n.Message,
                                 Status = n.Status.ToString(),
                                 CreatedAt = n.CreatedAt,
                                 SentAt = n.SentAt
                             })
                             .ToListAsync();
        }
    }
}
