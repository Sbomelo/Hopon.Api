using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services;
using Hopon.Api.Models.Enums;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace Hopon.Api.Pages.Admin.Trips
{
    public class IndexModel : PageModel
    {
        private readonly IAdminTripService _tripService;

        public IndexModel(IAdminTripService tripService)
        {
            _tripService = tripService;
        }

        public List<AdminTripListItemDto> Trips { get; set; } = new();
        public List<SelectOptionDto> RouteOptions { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public TripStatus? StatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? RouteFilter { get; set; }

        public async Task OnGetAsync()
        {
            RouteOptions = await _tripService.GetRouteOptionsAsync();
            Trips = await _tripService.GetTripsAsync(StatusFilter, RouteFilter);
        }
    }
}
