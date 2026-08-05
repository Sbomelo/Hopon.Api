using Hopon.Api.DTOs.Notifications;
using Hopon.Api.Models;

namespace Hopon.Api.Services.Interfaces
{
    public interface INotificationService
    {
        Task NotifyTripDelayedAsync(Trip trip, string reason);
        Task<List<NotificationDto>> GetNotificationsForUserAsync(int userId);
    }
}
