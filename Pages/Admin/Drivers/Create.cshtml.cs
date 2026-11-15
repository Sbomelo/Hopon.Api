using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin.Drivers;

public class CreateModel(AdminDriverService drivers) : PageModel
{
    [BindProperty] public CreateDriverInput Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        if (!await drivers.CreateAsync(Input))
        {
            ModelState.AddModelError("Input.Username", "That username is already in use.");
            return Page();
        }
        return RedirectToPage("Index");
    }
}
