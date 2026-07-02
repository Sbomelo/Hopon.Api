using Hopon.Api.Models.Enums;

namespace Hopon.Api.Models;

public class TripStop
{
    public int Id { get; set; }
     
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public int StopId { get; set; } 
    public Stop Stop { get; set;} = null!;

    public int SequenceOrder {get; set; } 

    public DateTime ScheduledArrival { get; set; }
    public DateTime? ScheduledDeparture { get; set; }
    public DateTime? ActualArrival { get; set; }
    public DateTime? ActualDeparture { get; set; }

    public TripStopStatus Status { get; set; } = TripStopStatus.Pending;
}