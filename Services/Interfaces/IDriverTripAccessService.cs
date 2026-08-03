using Hopon.Api.Models;

namespace Hopon.Api.Services.Interfaces
{
    public interface IDriverTripAccessService
    {
        Task<DriverTripAccessResult> CheckDriverTripAccessAsync(int driverId, int tripId);
        Task<Trip?> GetActiveTripForDriverAsync(int driverId);
    }
}
