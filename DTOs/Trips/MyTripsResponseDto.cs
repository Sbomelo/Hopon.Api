namespace Hopon.Api.DTOs.Trips
{
    public class MyTripsResponseDto
    {
        public List<MyTripDto> LiveTrips { get; set; } = new();
        public List<MyTripDto> PastTrips { get; set; } = new();
    }
}
