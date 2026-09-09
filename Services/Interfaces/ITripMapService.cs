using Hopon.Api.DTOs.Trips;

namespace Hopon.Api.Services.Interfaces
{
    public interface ITripMapService
    {
        Task<TripMapDataDto?> GetMapDataAsync(int tripId);
    }
}
