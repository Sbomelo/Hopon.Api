namespace Hopon.Api.Services.Interfaces;

public interface IAdminTicketService
{
    Task<(bool Success, string? Error)> SetActiveAsync(int tripId, int ticketId, bool active);
}
