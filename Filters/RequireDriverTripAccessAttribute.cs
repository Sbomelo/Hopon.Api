using System.Security.Claims;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hopon.Api.Filters;

public class RequireDriverTripAccessAttribute : TypeFilterAttribute
{
    public RequireDriverTripAccessAttribute() : base(typeof(TripAccessFilter))
    {

    }
}

public class DriverTripAccessFilter : IAsyncActionFilter
{
    private readonly ITripAccessService _accessService;

    public DriverTripAccessFilter(ITripAccessService accessService)
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


        var driverIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (driverIdClaim is null || !int.TryParse(driverIdClaim.Value, out var driverId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var result = await _accessService.CheckTripAccessAsync(driverId, tripId);

        if (!result.HasAccess)
        {
            context.Result = new NotFoundObjectResult(new { message = "Trip not found." });
            return;
        }

        context.HttpContext.Items["DriverTrip"] = result.Trip;

        await next();
    }
}
