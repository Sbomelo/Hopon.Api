using System.Security.Claims;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hopon.Api.Hubs;

[Authorize]
public class TripHub : Hub
{
    private readonly ITripAccessService _accessService;

    public TripHub(ITripAccessService accessService)
    {
        _accessService = accessService;
    }

    public async Task JoinTrip(int tripId)
    {
        var userId = GetUserId();
        var result = await _accessService.CheckTripAccessAsync(userId, tripId);

        if (!result.HasAccess)
            throw new HubException("You do not have access to this trip.");

        if (!result.IsTrackingActive)
            throw new HubException("Live tracking is not active for this trip.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tripId));
    }

    public async Task LeaveTrip(int tripId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tripId));
    }

    private int GetUserId()
    {
        var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is null || !int.TryParse(claim.Value, out var userId))
            throw new HubException("Unable to identify the calling user.");

        return userId;
    }

    public static string GroupName(int tripId) => $"trip-{tripId}";
}