using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Interfaces
{
    public interface IAdminTripService
    {
        Task<List<AdminTripListItemDto>> GetTripsAsync(TripStatus? statusFilter, int? routeFilter);
        Task<AdminTripDetailDto?> GetTripDetailAsync(int tripId);
        Task<int> CreateTripAsync(int busRouteId, int busId, int? driverId, DateTime scheduledDeparture, DateTime scheduledArrival);
        Task<bool> UpdateTripAsync(int tripId, int busRouteId, int busId, int? driverId, DateTime scheduledDeparture, DateTime scheduledArrival);
        Task<bool> UpdateTripStatusAsync(int tripId, TripStatus newStatus);
        Task<List<SelectOptionDto>> GetRouteOptionsAsync();
        Task<List<SelectOptionDto>> GetBusOptionsAsync();
        Task<List<SelectOptionDto>> GetDriverOptionsAsync();
    }
}
