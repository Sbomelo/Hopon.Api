namespace Hopon.Api.DTOs.RealTime
{
    public class LocationBroadCastDto
    {
        public int TripId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
