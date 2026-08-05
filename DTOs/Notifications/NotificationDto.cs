namespace Hopon.Api.DTOs.Notifications
{
    public class NotificationDto
    {
                public int Id { get; set; }
        public int? TripId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
