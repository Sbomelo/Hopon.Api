using Hopon.Api.DTOs;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Filters;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;

namespace Hopon.Api.Controllers;

[ApiController]
[Route("api/trips")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly ITripDashboardService _tripDashboardService;

    public TripsController(ITripDashboardService tripDashboardService)
    {
        _tripDashboardService = tripDashboardService;
    }


    [HttpGet("{tripId}/access-check")]
    [RequireTripAccess]
    public IActionResult CheckAccess(int tripId)
    {
        var result = (TripAccessResult)HttpContext.Items["TripAccessResult"]!;
        var (estimatedArrival, isEstimate) = EtaCalculator.CalculateEta(result.Trip!);

        return Ok(new TripAccessResponseDto
        {
            TripId = result.Trip!.Id,
            TicketReference = result.Ticket!.TicketReference,
            SeatNumber = result.Ticket.SeatNumber,
            TripStatus = result.Trip.Status.ToString(),
            IsTrackingActive = result.IsTrackingActive,
            ScheduledDeparture = result.Trip.ScheduledDeparture,
            ScheduledArrival = result.Trip.ScheduledArrival,
            EstimatedArrival = estimatedArrival,
            IsEstimate = isEstimate
        });
    }

    [HttpGet("my-trips")]
    public async Task<IActionResult> GetMyTrips()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _tripDashboardService.GetMyTripsAsync(userId);
        return Ok(result);
    }           
}