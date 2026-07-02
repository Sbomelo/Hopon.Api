namespace Hopon.Api.Models;

public class LocationUpdate
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip Trip {get; set;} = null!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}