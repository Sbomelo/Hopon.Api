using Hopon.Api.Extensions;
using Hopon.Api.Services;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.Pages.Admin.Trips
{
    public class EditModel : PageModel
    {

        private readonly IAdminTripService _tripService;
        private readonly IAdminTripActionsService _actions;

        public EditModel(IAdminTripService tripService, IAdminTripActionsService actions)
        {
            _tripService = tripService;
            _actions = actions;
        }

        [BindProperty] public string? DelayReason { get; set; }
        [TempData] public string? LifecycleError { get; set; }
        public TripStatus Status { get; set; }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public List<SelectListItem> RouteItems { get; set; } = new();
        public List<SelectListItem> BusItems { get; set; } = new();
        public List<SelectListItem> DriverItems { get; set; } = new();

        public class InputModel
        {
            [Required] public int BusRouteId { get; set; }
            [Required] public int BusId { get; set; }
            public int? DriverId { get; set; }
            [Required] public DateTime ScheduledDeparture { get; set; }
            [Required] public DateTime ScheduledArrival { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var trip = await _tripService.GetTripDetailAsync(Id);

            if (trip is null)
                return NotFound();

            Input = new InputModel
            {
                BusRouteId = trip.BusRouteId,
                BusId = trip.BusId,
                DriverId = trip.DriverId,
                ScheduledDeparture = SaTime.ToSa(trip.ScheduledDeparture),
                ScheduledArrival = SaTime.ToSa(trip.ScheduledArrival)
            };

            await LoadOptionsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if(Input.ScheduledArrival <= Input.ScheduledDeparture)
            {
                ModelState.AddModelError("Input.ScheduledArrival", "Arrival must be after departure.");
            }

            if (!ModelState.IsValid)
            {
                await LoadOptionsAsync();
                return Page();
            }

            try
            {
                var updated = await _tripService.UpdateTripAsync(Id, Input.BusRouteId, Input.BusId, 
                                                Input.DriverId, SaTime.ToUtc(Input.ScheduledDeparture), SaTime.ToUtc(Input.ScheduledArrival));

                if (!updated)
                    return NotFound();

                return RedirectToPage("./Details", new { id = Id });
            }
            catch (InvalidOperationException exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                await LoadOptionsAsync();
                return Page();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Could not save the trip. Please check your route, bus, and driver selections.");
                await LoadOptionsAsync();
                return Page();
            }
        }

        public Task<IActionResult> OnPostMarkDelayedAsync()
            => ChangeStatusAsync(AdminTripAction.Delay, DelayReason);

        public Task<IActionResult> OnPostResumeAsync()
            => ChangeStatusAsync(AdminTripAction.Resume);

        public Task<IActionResult> OnPostMarkCompletedAsync()
            => ChangeStatusAsync(AdminTripAction.Complete);

        private async Task<IActionResult> ChangeStatusAsync(AdminTripAction action, string? reason = null)
        {
            var (found, success, error) = await _actions.ApplyAsync(Id, action, reason);
            if (!found)
                return NotFound();
            if (!success)
            {
                LifecycleError = error;
                return RedirectToPage(new { id = Id });
            }
            return RedirectToPage("./Details", new { id = Id });
        }

        private async Task LoadOptionsAsync()
        {
            var trip = await _tripService.GetTripDetailAsync(Id);
            if (trip != null && Enum.TryParse<TripStatus>(trip.Status, out var status)) Status = status;
            RouteItems = (await _tripService.GetRouteOptionsAsync()).ToSelectListItems();
            BusItems = (await _tripService.GetBusOptionsAsync()).ToSelectListItems();
            DriverItems = (await _tripService.GetDriverOptionsAsync()).ToSelectListItems();
        }
    }
}
