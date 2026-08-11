using Hopon.Api.DTOs;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Filters;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
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
    private readonly IBoardingService _boardingService;
    private readonly ITripHistoryService _tripHistoryService;

    public TripsController(ITripDashboardService tripDashboardService, IBoardingService boardingService, ITripHistoryService tripHistoryService)
    {
        _tripDashboardService = tripDashboardService;
        _boardingService = boardingService;
        _tripHistoryService = tripHistoryService;
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

    [HttpGet("{tripdId}/boarding-status")]
    [RequireTripAccess]
    public async Task<IActionResult> GetBoardingStatus(int tripId)
    {
        var result = (TripAccessResult)HttpContext.Items["TripAccessResult"]!;
        var status = await _boardingService.GetStatusAsync(result.Ticket!.Id, tripId);

        return Ok(status);
    }

    [HttpPost("{tripId}/board")]
    [RequireTripAccess]
    public async Task<IActionResult> ConfirmBoarding(int tripId)
    {
        var result = (TripAccessResult)HttpContext.Items["TripAccessResult"]!;
        var (success, error, status) = await _boardingService.ConfirmBoardingAsync(
            result.Ticket!.Id, tripId, result.Trip!.Status);

        return success ? Ok(status) : BadRequest(error);
    }

    [HttpPost("{tripId}/alight")]
    [RequireTripAccess]
    public async Task<IActionResult> ConfirmAlighting(int tripId)
    {
        var result = (TripAccessResult)HttpContext.Items["TripAccessResult"]!;
        var (success, error, status) = await _boardingService.ConfirmAlightingAsync(
            result.Ticket!.Id, tripId, result.Trip!.Status);

        return success ? Ok(status) : BadRequest(error);
    }

    [HttpGet("{tripId}/history")]
    [RequireTripAccess]
    public async Task<IActionResult> GetTripHistory(int tripId)
    {
        var userIdClaims = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userIdClaims is null || !int.TryParse(userIdClaims, out var userId))
            return Unauthorized();

        var history = await _tripHistoryService.GetTripHistoryAsync(userId, tripId);

        if (history is null)
            return NotFound(new { message = "Trip history not found." });

        return Ok(history);
    }

}