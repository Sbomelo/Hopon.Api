using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Drivers;

public class IndexModel(AdminDriverService drivers) : PageModel
{
    public List<Models.Driver> Drivers { get; private set; } = new();
    public async Task OnGetAsync() => Drivers = await drivers.ListAsync();

    public async Task<IActionResult> OnPostSetActiveAsync(int id, bool isActive)
    {
        if (!await drivers.SetActiveAsync(id, isActive)) return NotFound();
        return RedirectToPage();
    }
}
