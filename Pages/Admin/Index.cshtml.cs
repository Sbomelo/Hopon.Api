using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin;

public class IndexModel(IAdminConsoleService service) : PageModel
{
    public AdminHomeDto Counts { get; private set; } = new();

    public async Task OnGetAsync() => Counts = await service.GetHomeAsync();
}
