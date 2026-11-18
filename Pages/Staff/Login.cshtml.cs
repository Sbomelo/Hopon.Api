using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Hopon.Api.Data;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Pages.Staff;

public class LoginModel(HoponDbContext db, IDriverAuthService driverAuth) : PageModel
{
    [BindProperty] public LoginInput Input { get; set; } = new();

    public class LoginInput
    {
        [Required] public string Role { get; set; } = "Admin";
        [Required, StringLength(100)] public string Username { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        int? id = null;
        string? name = null;
        if (ModelState.IsValid)
        {
            if (Input.Role == "Admin")
            {
                var admin = await db.AdminUsers.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Username == Input.Username.Trim() && a.IsActive);
                if (admin is not null && BCrypt.Net.BCrypt.Verify(Input.Password, admin.PasswordHash))
                {
                    id = admin.Id;
                    name = admin.Username;
                }
            }
            else if (Input.Role == "Driver")
            {
                var (success, _, driver) = await driverAuth.LoginAsync(Input.Username.Trim(), Input.Password);
                if (success && driver is not null)
                {
                    id = driver.Id;
                    name = driver.FullName;
                }
            }
        }

        if (id is null)
        {
            ModelState.Clear();
            Input.Password = string.Empty;
            ModelState.AddModelError(string.Empty, "Invalid username or password");
            return Page();
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.Value.ToString()),
            new Claim(ClaimTypes.Name, name!),
            new Claim(ClaimTypes.Role, Input.Role)
        }, StaffCookieEvents.Scheme);
        await HttpContext.SignInAsync(StaffCookieEvents.Scheme, new ClaimsPrincipal(identity));
        return RedirectToPage(Input.Role == "Admin" ? "/Admin/Index" : "/Driver/Trip");
    }
}
