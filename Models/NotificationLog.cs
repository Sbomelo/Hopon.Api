using Hopon.Api.Models.Enums;

namespace Hopon.Api.Models;

public class NotificationLog
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? TripId {get; set;}
    public Trip? Trip {get; set;}

    public NotificationType Type {get; set;}
    public NotificationChannel Channel {get; set;}
    public string Message {get; set;} = string.Empty;
    public NotificationStatus Status {get; set;} = NotificationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt {get; set;}
}