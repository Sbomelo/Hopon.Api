using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Interfaces
{
    public interface IBoardingService
    {
        Task<BoardingStatusDto> GetStatusAsync(int ticketId, int tripId);
        Task<(bool Success, string? Error, BoardingStatusDto? Status)> ConfirmBoardingAsync(int ticketId, int tripId, TripStatus tripStatus);
        Task<(bool Success, string? Error, BoardingStatusDto? Status)> ConfirmAlightingAsync(int ticketId, int tripId, TripStatus tripStatus);

    }
}
