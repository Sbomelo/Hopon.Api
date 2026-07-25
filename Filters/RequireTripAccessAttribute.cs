using System.Security.Claims;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hopon.Api.Filters;

public class RequireTripAccessAttribute : TypeFilterAttribute
{
    public RequireTripAccessAttribute() : base(typeof(TripAccessFilter))
    {

    }
}

public class TripAccessFilter : IAsyncActionFilter
{
    private readonly ITripAccessService _accessService;

    public TripAccessFilter(ITripAccessService accessService)
    {
        _accessService = accessService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.RouteData.Values.TryGetValue("tripId", out var raw) ||
            !int.TryParse(raw?.ToString(), out var tripId))
        {
            context.Result = new BadRequestObjectResult("A valid tripId route value is required.");
            return;
        }

       
        var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var result = await _accessService.CheckTripAccessAsync(userId, tripId);

        if (!result.HasAccess)
        {
            context.Result = new NotFoundObjectResult(new { message = "Trip not found." });
            return;
        }

        context.HttpContext.Items["TripAccessResult"] = result;

        await next();
    }
}