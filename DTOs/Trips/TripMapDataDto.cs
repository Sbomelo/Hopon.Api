using Microsoft.AspNetCore.Server.HttpSys;

namespace Hopon.Api.DTOs.Trips
{
    public class TripMapDataDto
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<MapStopDto> Stops { get; set; } = new();
        public MapLocationDto? LastKnownLocation { get; set; }
    }

    public class MapStopDto 
    {
        public int SequenceOrder { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class MapLocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime RecordedAt{ get; set; }
    }
}
