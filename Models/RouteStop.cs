namespace Hopon.Api.Models;

public class RouteStop
{
    public int Id {get; set;}

    public int BusRouteId{get; set;}
    public BusRoute BusRoute { get; set; } = null!;

    public int StopId { get; set; }
    public Stop Stop { get; set; } = null!;

    public int SequenceOrder {get; set;} //1 2 3
}