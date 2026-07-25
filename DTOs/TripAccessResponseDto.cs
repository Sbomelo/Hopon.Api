namespace Hopon.Api.DTOs
{
    public class TripAccessResponseDto
    {
        public int TripId { get; set;  }
        public string TicketReference { get; set; } = string.Empty;
        public string? SeatNumber { get; set; }
        public string TripStatus { get; set; } = string.Empty;
        public bool IsTrackingActive { get; set; }
        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
    }
}
