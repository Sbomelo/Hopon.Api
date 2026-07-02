using Hopon.Api.Models.Enums;

namespace  Hopon.Api.Models;

public class BoardingLog
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public Ticket Ticket {get; set; } = null!;

    public BoardingEventType EventType {get; set;}

    public int? StopId {get; set;} 
    public Stop? Stop { get; set; }

    public DateTime Timestamp {get; set;} = DateTime.UtcNow;
}