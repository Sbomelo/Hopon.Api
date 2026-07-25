using Hopon.Api.DTOs;
using Hopon.Api.DTOs;
using Hopon.Api.Filters;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hopon.Api.Controllers;

[ApiController]
[Route("api/trips")]
[Authorize]
public class TripsController : ControllerBase
{
    [HttpGet("{tripId}/access-check")]
    [RequireTripAccess]
    public IActionResult CheckAccess(int tripId)
    {
        var result = (TripAccessResult)HttpContext.Items["TripAccessResult"]!;

        return Ok(new TripAccessResponseDto
        {
            TripId = result.Trip!.Id,
            TicketReference = result.Ticket!.TicketReference,
            SeatNumber = result.Ticket.SeatNumber,
            TripStatus = result.Trip.Status.ToString(),
            IsTrackingActive = result.IsTrackingActive,
            ScheduledDeparture = result.Trip.ScheduledDeparture,
            ScheduledArrival = result.Trip.ScheduledArrival
        });
    }
}