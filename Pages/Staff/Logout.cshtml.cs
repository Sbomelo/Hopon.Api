using Hopon.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Staff;

public class LogoutModel : PageModel
{
    public IActionResult OnGet() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(StaffCookieEvents.Scheme);
        return RedirectToPage("/Staff/Login");
    }
}
