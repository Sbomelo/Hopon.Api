using Hopon.Api.DTOs.Trips;

namespace Hopon.Api.Services.Interfaces
{
    public interface ITripHistoryService
    {
        Task<TripHistoryDto?> GetTripHistoryAsync(int uerId, int tripId);
    }
}
