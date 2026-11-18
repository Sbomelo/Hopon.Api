using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Routes;

public class IndexModel(IAdminMasterDataService service) : PageModel
{
    public List<AdminRouteDto> Routes { get; private set; } = new();
    [TempData] public string? Error { get; set; }
    public async Task OnGetAsync() => Routes = await service.GetRoutesAsync();
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await service.DeleteRouteAsync(id);
        if (!result.Success) Error = result.Error;
        return RedirectToPage();
    }
}
