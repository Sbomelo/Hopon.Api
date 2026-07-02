using System.Security.Cryptography;

namespace Hopon.Api.Models;

public class BusRoute
{
    public int Id {get; set;}
    public string Name { get; set; } = string.Empty; //Dubarn - Johannesburg

    public ICollection<RouteStop> RouteStops {get; set;} = new List<RouteStop>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}