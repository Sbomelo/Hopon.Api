using Hopon.Api.DTOs.Admin;

namespace Hopon.Api.Services.Interfaces;

public interface IAdminTripActionsService
{
    Task<(bool Found, bool Success, string? Error)> ApplyAsync(int tripId, AdminTripAction action, string? reason = null);
}
