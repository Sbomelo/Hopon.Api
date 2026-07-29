namespace Hopon.Api.DTOs.Drivers
{
    public class DriverTripDto
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = null!;
        public string BusRegistraton { get; set; } = null!;
        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ActualDeparture { get; set; }
        public DateTime? ActualArrival { get; set; }
        public string Status { get; set; } = null!;
    }
}
