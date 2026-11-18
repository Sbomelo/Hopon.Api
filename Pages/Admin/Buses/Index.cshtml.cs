using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Buses;

public class IndexModel(IAdminMasterDataService service) : PageModel
{
    public List<AdminBusDto> Buses { get; private set; } = new();
    [TempData] public string? Error { get; set; }
    public async Task OnGetAsync() => Buses = await service.GetBusesAsync();
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await service.DeleteBusAsync(id);
        if (!result.Success) Error = result.Error;
        return RedirectToPage();
    }
}
