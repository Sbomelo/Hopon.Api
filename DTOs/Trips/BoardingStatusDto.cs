namespace Hopon.Api.DTOs.Trips
{
    public class BoardingStatusDto
    {
        public int TripId { get; set; }
        public bool HasBoarded { get; set; }
        public DateTime? BoardedAt { get; set; }
        public bool HasAlighted { get; set; }
        public DateTime? AlightedAt{ get; set; }
    }
}
