using Hopon.Api.DTOs.Admin;
using Hopon.Api.Extensions;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.Pages.Admin.Trips
{
    public class DetailsModel : PageModel
    {

        private readonly IAdminTripService _tripService;
        private readonly IAdminTripActionsService _actions;
        private readonly IAdminTicketService _tickets;

        public DetailsModel(IAdminTripService tripService, IAdminTripActionsService actions, IAdminTicketService tickets)
        {
            _tripService = tripService;
            _actions = actions;
            _tickets = tickets;
        }

        [BindProperty] public string? CancellationReason { get; set; }
        [TempData] public string? LifecycleError { get; set; }

        public AdminTripDetailDto? Trip { get; set; }
        public List<SelectListItem> PassengerItems { get; set; } = new();
        public List<SelectListItem> TripStopItems { get; set; } = new();

        public IssueTicketInput Input { get; set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        public class IssueTicketInput
        {
            [Required(ErrorMessage = "Select a passenger.")]
            public int? UserId { get; set; }

            [Required(ErrorMessage = "Select a destination stop.")]
            public int? DestinationTripStopId { get; set; }
            public int? OriginTripStopId { get; set; }

            [StringLength(10, ErrorMessage = "Seat number can be at most 10 characters.")]
            [RegularExpression(@"^[A-Za-z0-9\-]*$", ErrorMessage = "Seat number can only contain letters, numbers and hyphens.")]
            public string? SeatNumber { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!await LoadAsync(id))
                return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostCancelAsync(int id)
        {
            var (found, success, error) = await _actions.ApplyAsync(id, AdminTripAction.Cancel, CancellationReason);
            if (!found)
                return NotFound();
            if (!success) LifecycleError = error;
            return RedirectToPage("./Details", new { id });
        }

        public async Task<IActionResult> OnPostIssueTicketAsync(int id, IssueTicketInput input)
        {
            Input = input;
            if (!ModelState.IsValid)
                return await ReloadPageAsync(id);

            var (success, error, reference) = await _tripService.IssueTicketAsync(id, Input.UserId!.Value,
                Input.SeatNumber, Input.DestinationTripStopId!.Value, Input.OriginTripStopId);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, error!);
                return await ReloadPageAsync(id);
            }

            SuccessMessage = $"Ticket {reference} issued.";
            return RedirectToPage("./Details", new { id });
        }

        public Task<IActionResult> OnPostVoidAsync(int id, TicketActionInput ticketAction) => ChangeTicketAsync(id, ticketAction, false);
        public Task<IActionResult> OnPostReinstateAsync(int id, TicketActionInput ticketAction) => ChangeTicketAsync(id, ticketAction, true);

        private async Task<IActionResult> ChangeTicketAsync(int id, TicketActionInput ticketAction, bool active)
        {
            if (!ModelState.IsValid)
                return await ReloadPageAsync(id);
            if (await _tripService.GetTripDetailAsync(id) is null) return NotFound();
            var (success, error) = await _tickets.SetActiveAsync(id, ticketAction.TicketId, active);
            if (!success) LifecycleError = error;
            else SuccessMessage = active ? "Ticket reinstated." : "Ticket voided.";
            return RedirectToPage("./Details", new { id });
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
            TripStopItems = (await _tripService.GetTripStopOptionsAsync(id)).ToSelectListItems();
            return true;
        }
    }
}
