namespace Hopon.Api.Models;

public enum AccessAction { ViewedMap, ViewedHistory, JoinedLiveTracking, ViewedNotifications }
public class AccessAuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? TripId { get; set; }
    public AccessAction Action { get; set; }
    public DateTime CreatedAt { get; set; }
}
