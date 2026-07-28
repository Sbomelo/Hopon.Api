namespace Hopon.Api.DTOs.Admin
{
    public class AdminTripListItemDto
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string BusRegistration { get; set; } = string.Empty;
        public string? DriverName { get; set; }
        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public string Status { get; set; } = string.Empty;
        public int TicketsSold { get; set; }
    }
}
