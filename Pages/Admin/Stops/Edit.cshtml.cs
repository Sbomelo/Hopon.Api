using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Stops;

public class EditModel(IAdminMasterDataService service) : PageModel
{
    [BindProperty] public StopInput Input { get; set; } = new();
    public string? LocationUrl => Input.Latitude.HasValue && Input.Longitude.HasValue
        ? new AdminStopDto { Latitude = Input.Latitude, Longitude = Input.Longitude }.LocationUrl : null;
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            var stop = await service.GetStopAsync(id.Value);
            if (stop is null) return NotFound();
            Input = stop;
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!ModelState.IsValid) return Page();
        var result = await service.SaveStopAsync(id, Input);
        if (result.Success) return RedirectToPage("./Index");
        ModelState.AddModelError(string.Empty, result.Error!);
        return Page();
    }
}
