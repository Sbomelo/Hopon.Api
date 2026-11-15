using System.Security.Claims;
using Hopon.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class StaffCookieEvents(HoponDbContext db) : CookieAuthenticationEvents
{
    public const string Scheme = "StaffCookie";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var active = false;
        if (int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            if (principal!.IsInRole("Admin"))
                active = await db.AdminUsers.AnyAsync(a => a.Id == id && a.IsActive);
            else if (principal.IsInRole("Driver"))
                active = await db.Drivers.AnyAsync(d => d.Id == id && d.IsActive);
        }

        if (!active)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }
}
