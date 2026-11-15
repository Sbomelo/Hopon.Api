using Hopon.Api.Models;

namespace Hopon.Api.Services.Interfaces
{
    public interface IDriverTripService
    {
        Task<(bool Success, string? Error)> StartTripAsync(Trip trip);
        Task<(bool Success, string? Error)> ReportDelayedAsync(Trip trip, string reason);
        Task<(bool Success, string? Error)> ResumeFromDelayAsync(Trip trip);
        Task<(bool Success, string? Error)> CompletedAsync(Trip trip);
        Task<(bool Success, string? Error)> RecordLocationAsync(Trip trip, double latitude, double longitude);
        Task<(bool Success, string? Error)> MarkStopArrivedAsync(Trip trip, int tripStopId);
        Task<(bool Success, string? Error)> MarkStopDepartedAsync(Trip trip, int tripStopId);

    }
}
