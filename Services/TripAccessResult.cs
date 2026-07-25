using Hopon.Api.Models;

namespace Hopon.Api.Services
{
    public class TripAccessResult
    {
        public bool HasAccess { get; set; }
        public bool  IsTrackingActive { get; set; }
        public Ticket? Ticket { get; set; }
        public Trip? Trip { get; set; }

    }
}
