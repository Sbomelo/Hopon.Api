namespace Hopon.Api.DTOs.Drivers
{
    public class DriverTripPageDto
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string BusRegistration { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ActualDeparture { get; set; }
        public DateTime? ActualArrival { get; set; }

        public List<DriverStopDto> Stops { get; set; } = new();

        public DriverStopDto? NextPendingStop { get; set; }
        public DriverStopDto? CurrentArrivedStop { get; set; }

        public double? LastLatitude { get; set; }
        public double? LastLongitude { get; set; }
        public DateTime? LastLocationAt { get; set; }
    }

    public class DriverStopDto
    {
        public int TripStopId { get; set; }
        public string StopName { get; set; } = string.Empty;
        public int SequenceOrder { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ActualArrival { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
