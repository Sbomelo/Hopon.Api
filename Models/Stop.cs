using Hopon.Api.Models.Enums;

namespace Hopon.Api.Models;

public class Stop
{
    public int Id { get; set; }
    public string Name {get; set;} = string.Empty;
    public double Latitude {get ; set;} 
    public double Longitude {get; set;}

    public ICollection<RouteStop> RouteStops {get; set;} = new List<RouteStop>();
    public ICollection<TripStop> TripStops {get; set;} = new List<TripStop>();
}