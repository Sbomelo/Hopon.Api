using Hopon.Api.Extensions;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Hopon.Api.Pages.Admin.Trips
{
    public class CreateModel : PageModel
    {
        private readonly IAdminTripService _tripService;

        public CreateModel (IAdminTripService tripService)
        {
            _tripService = tripService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<SelectListItem> RouteItems { get; set; } = new();
        public List<SelectListItem> BusItems { get; set; } = new();
        public List<SelectListItem> DriverItems { get; set; } = new();

        public class InputModel
        {
            [Required]
            public int BusRouteId { get; set; }

            [Required]
            public int BusId { get; set; }

            public int? DriverId { get; set; }

            [Required]
            public DateTime ScheduledDeparture { get; set; }

            [Required]
            public DateTime ScheduledArrival { get; set; }

        }

        public async Task OnGetAsync()
        {
            await LoadOptionsAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if(Input.ScheduledArrival <= Input.ScheduledDeparture)
            {
                ModelState.AddModelError(nameof(Input.ScheduledArrival), "Arrival must be after departure");
            }

            if (!ModelState.IsValid)
            {
                await LoadOptionsAsync();
                return Page();
            }
            try
            {
                var tripId = await _tripService.CreateTripAsync(
                                        Input.BusRouteId, Input.BusId, Input.DriverId,
                                        Input.ScheduledDeparture, Input.ScheduledArrival);

                return RedirectToPage("./Details", new { id = tripId });

            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Could not create the trip. Please check your route, bus, and driver selections");
                await LoadOptionsAsync();
                return Page();
            }
        }

        private async Task LoadOptionsAsync()
        {
            RouteItems = (await _tripService.GetRouteOptionsAsync()).ToSelectListItems();
            BusItems = (await _tripService.GetBusOptionsAsync()).ToSelectListItems();
            DriverItems = (await _tripService.GetDriverOptionsAsync()).ToSelectListItems();
        }
    }
}
