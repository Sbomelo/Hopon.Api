using Hopon.Api.Models;

namespace Hopon.Api.Services
{
    public class DriverTripAccessResult
    {
        public bool HasAccess { get; set; }
        public Trip? Trip { get; set; }
    }
}
