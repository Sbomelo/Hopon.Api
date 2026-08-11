namespace Hopon.Api.DTOs.Trips
{
    public class TripHistoryDto
    {
        public int TripId { get; set; }
        public string TicketReference { get; set; } = string.Empty;
        public string? SeatNumber { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ActualDeparture { get; set; }
        public DateTime? ActualArrival { get; set; }

        public bool HasBoarded { get; set; }
        public DateTime? BoardedAt { get; set; }
        public bool HasAlighted { get; set; }
        public DateTime? AlightedAt { get; set; }

        public List<TripHistoryStopDto> Stops { get; set; } = new();
    }

    public class TripHistoryStopDto
    {
        public int SequenceOrder { get; set; }
        public string StopName { get; set; } = string.Empty;
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ScheduledDeparture { get; set; }
        public DateTime? ActualArrival { get; set; }
        public DateTime? ActualDeparture { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
