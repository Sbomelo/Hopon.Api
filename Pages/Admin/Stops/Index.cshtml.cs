using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Stops;

public class IndexModel(IAdminMasterDataService service) : PageModel
{
    public List<AdminStopDto> Stops { get; private set; } = new();
    [TempData] public string? Error { get; set; }
    public async Task OnGetAsync() => Stops = await service.GetStopsAsync();
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await service.DeleteStopAsync(id);
        if (!result.Success) Error = result.Error;
        return RedirectToPage();
    }
}
