using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;

namespace Hopon.Api.Services;

public class AdminTripActionsService(HoponDbContext db, ITripLifecycleService lifecycle) : IAdminTripActionsService
{
    public async Task<(bool Found, bool Success, string? Error)> ApplyAsync(int tripId, AdminTripAction action, string? reason = null)
    {
        var trip = await db.Trips.FindAsync(tripId);
        if (trip is null) return (false, false, "Trip not found.");
        var result = action switch
        {
            AdminTripAction.Delay => await lifecycle.ReportDelayedAsync(trip, reason ?? string.Empty),
            AdminTripAction.Resume => await lifecycle.ResumeFromDelayAsync(trip),
            AdminTripAction.Complete => await lifecycle.CompletedAsync(trip),
            AdminTripAction.Cancel => await lifecycle.CancelAsync(trip, reason),
            _ => (false, "Unknown trip action.")
        };
        return (true, result.Item1, result.Item2);
    }
}
