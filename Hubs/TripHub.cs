using System.Security.Claims;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hopon.Api.Hubs;

[Authorize(Roles = "Passenger")]
public class TripHub : Hub
{
    private readonly ITripAccessService _accessService;
    private readonly TripTrackingSessions _sessions;

    public TripHub(ITripAccessService accessService, TripTrackingSessions sessions)
    {
        _accessService = accessService;
        _sessions = sessions;
    }

    public async Task JoinTrip(int tripId)
    {
        var userId = GetUserId();
        await _sessions.JoinAsync(userId, tripId, Context.ConnectionId, async () =>
        {
            var result = await _accessService.CheckTripAccessAsync(userId, tripId);

        if (!result.HasAccess)
            throw new HubException("You do not have access to this trip.");

        if (!result.IsTrackingActive)
            throw new HubException("Live tracking is not active for this trip.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tripId));
        });
    }

    public async Task LeaveTrip(int tripId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tripId));
        await _sessions.LeaveAsync(Context.ConnectionId, tripId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _sessions.LeaveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
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
