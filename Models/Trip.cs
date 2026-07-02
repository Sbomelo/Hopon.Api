using Hopon.Api.Models.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Client;

namespace Hopon.Api.Models;

public class Trip
{
    public int Id { get; set;}

    public int BusRouteId { get; set; }
    public BusRoute BusRoute {get; set;} = null!;

    public int BusId {get; set;}
    public Bus Bus {get; set;} = null!;

    public int? DriverId {get; set;}
    public Driver? Driver { get; set;}

    public DateTime ScheduledDeparture { get; set;}
    public DateTime ScheduledArrival {get; set;}
    public DateTime? ActualDeparture { get; set;}
    public DateTime? ActualArrival {get; set;}

    public TripStatus Status {get; set;} = TripStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    public ICollection<TripStop> TripStops {get; set;} = new List<TripStop>();
    public ICollection<Ticket> Tickets {get; set; } = new List<Ticket>();
    public ICollection<LocationUpdate> LocationUpdates {get; set; } = new List<LocationUpdate>();
}
