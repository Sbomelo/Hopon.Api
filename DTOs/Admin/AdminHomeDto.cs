namespace Hopon.Api.DTOs.Admin;

public class AdminHomeDto
{
    public int InProgress { get; set; }
    public int Delayed { get; set; }
    public int ScheduledToday { get; set; }
    public int TicketsSoldToday { get; set; }
    public int NotificationsSent { get; set; }
    public int NotificationsFailed { get; set; }
}
