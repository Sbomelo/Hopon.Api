using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Trips
{
    public class DetailsModel : PageModel
    {

        private readonly IAdminTripService _tripService;

        public DetailsModel(IAdminTripService tripService)
        {
            _tripService = tripService;
        }

        public AdminTripDetailDto? Trip { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Trip = await _tripService.GetTripDetailAsync(id);

            if (Trip is null)
                return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostCancelAsync(int id)
        {
            await _tripService.UpdateTripStatusAsync(id, TripStatus.Cancelled);
            return RedirectToPage("./Details", new { id });
        }
    }
}
