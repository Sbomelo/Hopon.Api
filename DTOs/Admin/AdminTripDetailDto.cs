namespace Hopon.Api.DTOs.Admin;

public class AdminTripDetailDto
{
    public int TripId { get; set; }
    public int BusRouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public int BusId { get; set; }
    public string BusRegistration { get; set; } = string.Empty;
    public int? DriverId { get; set; }
    public string? DriverName { get; set; }
    public DateTime ScheduledDeparture { get; set; }
    public DateTime ScheduledArrival { get; set; }
    public DateTime? ActualDeparture { get; set; }
    public DateTime? ActualArrival { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<AdminTicketSummaryDto> Tickets { get; set; } = new();
}

public class AdminTicketSummaryDto
{
    public string TicketReference { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public bool IsActive { get; set; }
}
