using Hopon.Api.DTOs.Notifications;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Interfaces
{
    public interface INotificationService
    {
        Task CreateAsync(int userId, int? tripId, NotificationType type, string message, NotificationChannel[] channels);
        Task NotifyTripDelayedAsync(Trip trip, string reason);
        Task NotifyStopArrivedAsync(TripStop stop);
        Task NotifyTripCompletedAsync(Trip trip);
        Task<List<NotificationDto>> GetNotificationsForUserAsync(int userId);
    }
}
