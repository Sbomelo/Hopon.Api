namespace Hopon.Api.DTOs.RealTime
{
    public class TripStatusBroadcastDto
    {
        public int TripId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime? ActualDeparture { get; set; }
        public DateTime? ActualArrival { get; set; }

    }
}
