using Hopon.Api.DTOs.Trips;

namespace Hopon.Api.Services
{
    public interface ITripDashboardService
    {
        Task<MyTripsResponseDto> GetMyTripsAsync(int userId);
    }
}
