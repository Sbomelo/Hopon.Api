using System.ComponentModel.DataAnnotations;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Drivers;

public class ResetPasswordModel(AdminDriverService drivers) : PageModel
{
    public Models.Driver Driver { get; private set; } = null!;
    [BindProperty, Required, MinLength(8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var driver = await drivers.FindAsync(id);
        if (driver is null) return NotFound();
        Driver = driver;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var driver = await drivers.FindAsync(id);
        if (driver is null) return NotFound();
        Driver = driver;
        if (!ModelState.IsValid) return Page();
        if (!await drivers.ResetPasswordAsync(id, Password)) return NotFound();
        return RedirectToPage("Index");
    }
}
