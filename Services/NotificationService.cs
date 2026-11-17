using Hopon.Api.Data;
using Hopon.Api.DTOs.Notifications;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Hopon.Api.Services.Sms;

namespace Hopon.Api.Services
{
    public class NotificationService : INotificationService
    {
        private readonly HoponDbContext _db;
        private readonly PassengerNotificationWriter _writer;
        private readonly TimeProvider _timeProvider;
        private readonly ISmsSender _smsSender;
        private readonly NotificationOptions _options;

        public NotificationService(HoponDbContext db, PassengerNotificationWriter writer, TimeProvider timeProvider,
            ISmsSender smsSender, IOptions<NotificationOptions> options)
        {
            _db = db;
            _writer = writer;
            _timeProvider = timeProvider;
            _smsSender = smsSender;
            _options = options.Value;
        }

        public async Task CreateAsync(int userId, int? tripId, NotificationType type, string message, NotificationChannel[] channels)
        {
            foreach (var channel in channels.Distinct())
            {
                if (channel == NotificationChannel.Sms && !_options.SendsSms(type))
                    continue;

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var log = new NotificationLog
                {
                    UserId = userId, TripId = tripId, Type = type, Channel = channel, Message = message,
                    CreatedAt = now, Status = channel == NotificationChannel.InApp ? NotificationStatus.Sent : NotificationStatus.Pending,
                    SentAt = channel == NotificationChannel.InApp ? now : null
                };
                // Claim the channel before sending so concurrent ticks cannot both send SMS.
                if (!await _writer.TryWriteAsync(log))
                    return;
                if (channel != NotificationChannel.Sms)
                    continue;

                var phone = await _db.Users.Where(u => u.Id == userId).Select(u => u.PhoneNumber).SingleAsync();
                var result = await _smsSender.SendAsync(phone, message);
                log.Status = result.Success ? NotificationStatus.Sent : NotificationStatus.Failed;
                log.SentAt = result.Success ? _timeProvider.GetUtcNow().UtcDateTime : null;
                await _db.SaveChangesAsync();
            }
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
            foreach (var userId in userIds)
            {
                if (alreadySent.Contains(userId))
                    continue;

                await CreateAsync(userId, stop.TripId, NotificationType.Arrival,
                    NotificationMessages.Arrival(stop.Stop.Name), [NotificationChannel.InApp, NotificationChannel.Sms]);
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

            var message = string.IsNullOrWhiteSpace(reason)
                                 ? "Your bus has been delayed."
                                 : $"Your bus has been delayed: {reason}";

            foreach (var userId in userIds)
                await CreateAsync(userId, trip.Id, NotificationType.Delay, message,
                    [NotificationChannel.InApp, NotificationChannel.Sms]);
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
