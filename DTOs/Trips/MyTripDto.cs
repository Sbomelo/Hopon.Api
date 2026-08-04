using System.Security.Principal;

namespace Hopon.Api.DTOs.Trips
{
    public class MyTripDto
    {
        public int TripId { get; set; }
        public string TicketReference { get; set; } = null!;
        public string RouteName { get; set; } = null!;
        public DateTime TripDate{ get; set; }
        public DateTime ScheduledDeparture { get; set; }
        public DateTime ScheduledArrival { get; set; }
        public DateTime? ActualDepature { get; set; }
        public DateTime? ActualArrival { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public bool IsEstimate { get; set; }
        public string Status { get; set; } = null!;
        public bool IsLive { get; set; }
    }
}
