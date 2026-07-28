using Hopon.Api.Extensions;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
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

        public EditModel(IAdminTripService tripService)
        {
            _tripService = tripService;
        }

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
                ScheduledDeparture = trip.ScheduledDeparture,
                ScheduledArrival = trip.ScheduledArrival
            };

            await LoadOptionsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if(Input.ScheduledArrival <= Input.ScheduledDeparture)
            {
                ModelState.AddModelError(nameof(Input.ScheduledArrival), "Arrival must be afetr  departure");
            }

            if (!ModelState.IsValid)
            {
                await LoadOptionsAsync();
                return Page();
            }

            try
            {
                var updated = await _tripService.UpdateTripAsync(Id, Input.BusRouteId, Input.BusId, 
                                                Input.DriverId, Input.ScheduledDeparture, Input.ScheduledArrival);

                if (!updated)
                    return NotFound();

                return RedirectToPage("./Details", new { id = Id });
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Could not save the trip. Please check your route, bus, and driver selections.");
                await LoadOptionsAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostMarkDelayedAsync()
        {
            await _tripService.UpdateTripStatusAsync(Id, TripStatus.Delayed);
            return RedirectToPage("./Details", new { id = Id });
        }

        public async Task<IActionResult> OnPostMarkCompletedAsync()
        {
            await _tripService.UpdateTripStatusAsync(Id, TripStatus.Completed);
            return RedirectToPage("./Details", new { id = Id });
        }

        private async Task LoadOptionsAsync()
        {
            RouteItems = (await _tripService.GetRouteOptionsAsync()).ToSelectListItems();
            BusItems = (await _tripService.GetBusOptionsAsync()).ToSelectListItems();
            DriverItems = (await _tripService.GetDriverOptionsAsync()).ToSelectListItems();
        }
    }
}
