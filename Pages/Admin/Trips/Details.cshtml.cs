using Hopon.Api.DTOs.Admin;
using Hopon.Api.Extensions;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

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
        public List<SelectListItem> PassengerItems { get; set; } = new();

        [BindProperty]
        public IssueTicketInput Input { get; set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        public class IssueTicketInput
        {
            [Required(ErrorMessage = "Select a passenger.")]
            public int? UserId { get; set; }

            [StringLength(10, ErrorMessage = "Seat number can be at most 10 characters.")]
            [RegularExpression(@"^[A-Za-z0-9\-]*$", ErrorMessage = "Seat number can only contain letters, numbers and hyphens.")]
            public string? SeatNumber { get; set; }
        }

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

        public async Task<IActionResult> OnPostIssueTicketAsync(int id)
        {
            if (!ModelState.IsValid)
                return await ReloadPageAsync(id);

            try
            {
                var (success, error, reference) = await _tripService.IssueTicketAsync(id, Input.UserId!.Value, Input.SeatNumber);

                if (!success)
                {
                    ModelState.AddModelError(string.Empty, error!);
                    return await ReloadPageAsync(id);
                }

                SuccessMessage = $"Ticket {reference} issued.";
                return RedirectToPage("./Details", new { id });
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Could not issue the ticket. Someone may have just issued one for this passenger or seat. Please try again.");
                return await ReloadPageAsync(id);
            }
        }

        private async Task<IActionResult> ReloadPageAsync(int id)
        {
            if (!await LoadAsync(id))
                return NotFound();

            return Page();
        }

        private async Task<bool> LoadAsync(int id)
        {
            Trip = await _tripService.GetTripDetailAsync(id);

            if (Trip is null)
                return false;

            PassengerItems = (await _tripService.GetAvailablePassengerOptionsAsync(id)).ToSelectListItems();
            return true;
        }
    }
}
