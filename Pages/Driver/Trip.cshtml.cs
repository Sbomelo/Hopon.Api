using System.Security.Claims;
using Hopon.Api.Data;
using Hopon.Api.Services;
using Hopon.Api.DTOs.Drivers;
using Hopon.Api.Models;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Pages.Driver
{
    public class TripModel : PageModel
    {
        private readonly IDriverTripAccessService _accessService;
        private readonly ITripLifecycleService _tripService;
        private readonly HoponDbContext _db;

        private int DriverId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        public DriverTripPageDto? Trip { get; set; }
        public string? ActionMessage { get; set; }
        public bool ActionSuccess { get; set; }

        [BindProperty] public string DelayReason { get; set; } = string.Empty;
        [BindProperty] public int LocationTripId { get; set; }
        [BindProperty] public double Latitude { get; set; }
        [BindProperty] public double Longitude { get; set; }
        [BindProperty] public int TripStopId { get; set; }

        public TripModel(
            IDriverTripAccessService accessService,
            ITripLifecycleService tripService,
            HoponDbContext db)
        {
            _accessService = accessService;
            _tripService = tripService;
            _db = db;
        }

        public async Task OnGetAsync() =>
            Trip = await BuildPageDtoAsync();

        public async Task<IActionResult> OnPostPostLocationAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            if (LocationTripId != trip.Id) return BadRequest(new { message = "Your assigned trip changed. Reload Hopon before sharing location." });
            if (!Request.Form.ContainsKey("Latitude") || !Request.Form.ContainsKey("Longitude") ||
                ModelState[nameof(Latitude)]?.Errors.Count > 0 || ModelState[nameof(Longitude)]?.Errors.Count > 0)
                return BadRequest(new { message = "Please provide valid GPS coordinates." });
            var result = await _tripService.RecordLocationAsync(trip, Latitude, Longitude);
            var message = result.Error ?? "Location shared with passengers.";
            if (Request.Headers["X-Requested-With"] == "fetch")
                return new JsonResult(new { message }) { StatusCode = result.Outcome == LocationOutcome.Recorded ? 200 : result.Outcome == LocationOutcome.Throttled ? 429 : 400 };
            Response.StatusCode = result.Outcome == LocationOutcome.Recorded ? 200 : result.Outcome == LocationOutcome.Throttled ? 429 : 400;
            return await Refresh(result.Outcome == LocationOutcome.Recorded, message);
        }

        public async Task<IActionResult> OnPostMarkArrivedAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.MarkStopArrivedAsync(trip, TripStopId);
            if (!ok) Response.StatusCode = StatusCodes.Status400BadRequest;
            return await Refresh(ok, ok ? "✅ Arrived at stop." : err!);
        }

        public async Task<IActionResult> OnPostMarkDepartedAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.MarkStopDepartedAsync(trip, TripStopId);
            if (!ok) Response.StatusCode = StatusCodes.Status400BadRequest;
            return await Refresh(ok, ok ? "🚏 Departed from stop." : err!);
        }

        public async Task<IActionResult> OnPostReportDelayAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.ReportDelayedAsync(trip, DelayReason ?? string.Empty);
            return await Refresh(ok, ok ? "⚠️ Delay reported — passengers have been notified." : err!);
        }

        public async Task<IActionResult> OnPostResumeAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.ResumeFromDelayAsync(trip);
            return await Refresh(ok, ok ? "✅ Trip resumed — delay cleared." : err!);
        }

        public async Task<IActionResult> OnPostStartTripAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.StartTripAsync(trip);
            return await Refresh(ok, ok ? "▶ Trip started!" : err!);
        }

        public async Task<IActionResult> OnPostCompleteTripAsync()
        {
            var trip = await LoadTrackedTripAsync();
            if (trip is null) return await NoTripResult();

            var (ok, err) = await _tripService.CompletedAsync(trip);
            return await Refresh(ok, ok ? "🏁 Trip completed." : err!);
        }

        private async Task<Trip?> LoadTrackedTripAsync() =>
            await _accessService.GetActiveTripForDriverAsync(DriverId);

        private async Task<DriverTripPageDto?> BuildPageDtoAsync()
        {
            var trip = await _accessService.GetActiveTripWithStopsForDriverAsync(DriverId);
            if (trip is null) return null;

            var lastLocation = await _db.LocationUpdates
                .Where(lu => lu.TripId == trip.Id)
                .OrderByDescending(lu => lu.RecordedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var orderedStops = trip.TripStops.OrderBy(ts => ts.SequenceOrder).ToList();

            return new DriverTripPageDto
            {
                TripId = trip.Id,
                RouteName = trip.BusRoute.Name,
                BusRegistration = trip.Bus.RegistrationNumber,
                Status = trip.Status.ToString(),
                ScheduledDeparture = trip.ScheduledDeparture,
                ScheduledArrival = trip.ScheduledArrival,
                ActualDeparture = trip.ActualDeparture,
                ActualArrival = trip.ActualArrival,
                Stops = orderedStops.Select(ts => new DriverStopDto
                {
                    TripStopId = ts.Id,
                    StopName = ts.Stop.Name,
                    SequenceOrder = ts.SequenceOrder,
                    ScheduledArrival = ts.ScheduledArrival,
                    ActualArrival = ts.ActualArrival,
                    Status = ts.Status.ToString()
                }).ToList(),
                NextPendingStop = orderedStops
                    .Where(ts => ts.Status == Models.Enums.TripStopStatus.Pending)
                    .Select(ts => new DriverStopDto
                    {
                        TripStopId = ts.Id,
                        StopName = ts.Stop.Name,
                        SequenceOrder = ts.SequenceOrder,
                        ScheduledArrival = ts.ScheduledArrival,
                        Status = ts.Status.ToString()
                    })
                    .FirstOrDefault(),
                CurrentArrivedStop = orderedStops
                    .Where(ts => ts.Status == Models.Enums.TripStopStatus.Arrived)
                    .Select(ts => new DriverStopDto
                    {
                        TripStopId = ts.Id,
                        StopName = ts.Stop.Name,
                        SequenceOrder = ts.SequenceOrder,
                        ActualArrival = ts.ActualArrival,
                        Status = ts.Status.ToString()
                    })
                    .FirstOrDefault(),
                LastLatitude = lastLocation?.Latitude,
                LastLongitude = lastLocation?.Longitude,
                LastLocationAt = lastLocation?.RecordedAt
            };
        }

        private async Task<IActionResult> Refresh(bool success, string message)
        {
            Trip = await BuildPageDtoAsync();
            ActionSuccess = success;
            ActionMessage = message;
            return Page();
        }

        private async Task<IActionResult> NoTripResult()
        {
            Trip = null;
            ActionSuccess = false;
            ActionMessage = "No active trip is assigned to you.";
            return Page();
        }
    }
 
}
