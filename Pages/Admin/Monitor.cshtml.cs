using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MonitorModel(IAdminConsoleService service) : PageModel
{
    public List<AdminMonitorDto> Trips { get; private set; } = new();
    public async Task OnGetAsync() => Trips = await service.GetMonitorAsync();
    public async Task<JsonResult> OnGetTripsAsync() => new(await service.GetMonitorAsync());
}
