using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Buses;

public class EditModel(IAdminMasterDataService service) : PageModel
{
    [BindProperty] public BusInput Input { get; set; } = new();
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            var bus = await service.GetBusAsync(id.Value);
            if (bus is null) return NotFound();
            Input = bus;
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!ModelState.IsValid) return Page();
        var result = await service.SaveBusAsync(id, Input);
        if (result.Success) return RedirectToPage("./Index");
        ModelState.AddModelError(string.Empty, result.Error!);
        return Page();
    }
}
