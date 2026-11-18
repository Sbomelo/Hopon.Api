using System.ComponentModel.DataAnnotations;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.DTOs.Admin;

public class NotificationLogFilter
{
    [Range(1, int.MaxValue)] public int? TripId { get; set; }
    [StringLength(100)] public string? Passenger { get; set; }
    [EnumDataType(typeof(NotificationType))] public NotificationType? Type { get; set; }
    [EnumDataType(typeof(NotificationChannel))] public NotificationChannel? Channel { get; set; }
    [EnumDataType(typeof(NotificationStatus))] public NotificationStatus? Status { get; set; }
    [Range(1, int.MaxValue)] public int PageNumber { get; set; } = 1;
}

public class AdminNotificationLogDto
{
    public const int PageSize = 50;
    public int PageNumber { get; set; } = 1;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public List<AdminNotificationRowDto> Items { get; set; } = new();
}

public class AdminNotificationRowDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Passenger { get; set; } = string.Empty;
    public string? Route { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}
