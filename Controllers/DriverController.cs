using Hopon.Api.DTOs.Drivers;
using Hopon.Api.Filters;
using Hopon.Api.Models;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hopon.Api.Controllers
{
    [Route("api/driver")]
    [ApiController]
    [Authorize(Roles ="Driver")]
    public class DriverController : ControllerBase
    {
        private readonly IDriverTripAccessService _accessService;
        private readonly IDriverTripService _tripService;

        public DriverController(IDriverTripAccessService accessService, IDriverTripService tripService)
        {
            _accessService = accessService;
            _tripService = tripService;
        }

        [HttpGet("my-trip")]
        public async Task<IActionResult> GetMyTrip()
        {
            var driverId = GetDriverId();

            if (driverId is null)
                return NotFound();

            var trip = await _accessService.GetActiveTripForDriverAsync(driverId.Value);

            if (trip is null)
                return NotFound(new { message = "No active trip assigned." });

            return Ok(new DriverTripDto
            {
                TripId = trip.Id,
                RouteName = trip.BusRoute.Name,
                BusRegistraton = trip.Bus.RegistrationNumber,
                ScheduledDeparture = trip.ScheduledDeparture,
                ScheduledArrival = trip.ScheduledArrival,
                ActualArrival = trip.ActualArrival,
                ActualDeparture = trip.ActualArrival,
                Status = trip.Status.ToString()
            });
        }

        [HttpPost("trips/{tripId}/start")]
        [RequireDriverTripAccess]
        public async Task<IActionResult> StartTrip(int tripId)
        {
            var trip = (Trip)HttpContext.Items["DriverTrip"]!;
            var (success, error) = await _tripService.StartTripAsync(trip);

            return success ? Ok(new { message = "Trip started.", status = trip.Status.ToString() })
                           : BadRequest(error);
        }

        [HttpPost("trips/{tripId}/delay")]
        [RequireDriverTripAccess]
        public async Task<IActionResult> ReportDelay(int tripId, [FromBody] DelayedRequestDto dto)
        {
            var trip = (Trip)HttpContext.Items["DriverTrip"]!;
            var (success, error) = await _tripService.ReportDelayedAsync(trip, dto.Reason);

            return success ? Ok(new { message = "Delay reported.", status = trip.Status.ToString() })
                           : BadRequest(error);
        }

        [HttpPost("trips/{tripId}/complete")]
        [RequireDriverTripAccess]
        public async Task<IActionResult> CompleteTrip(int tripId)
        {
            var trip = (Trip)HttpContext.Items["DriverTrip"]!;
            var (success, error) = await _tripService.CompletedAsync(trip);

            return success ? Ok(new { message = "Trip completed.", status = trip.Status.ToString() })
                           : BadRequest(error);
        }

        [HttpPost("trips/{tripId}/location")]
        [RequireDriverTripAccess]
        public async Task<IActionResult> PostLocation(int tripId, [FromBody] LocationRequestDto dto)
        {
            var trip = (Trip)HttpContext.Items["DriverTrip"]!;
            var (success, error) = await _tripService.RecordLocationAsync(trip, dto.Latitude, dto.Longitude);

            return success ? Ok(new { message = "Location recorded." })
                           : BadRequest(error);
        }

        private int? GetDriverId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim is not null && int.TryParse(claim.Value, out var id) ? id : null;
        }
    }
}
