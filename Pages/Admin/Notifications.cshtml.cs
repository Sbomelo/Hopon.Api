using System.Globalization;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Extensions;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hopon.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationsModel(IAdminConsoleService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public NotificationLogFilter Filter { get; set; } = new();
    public AdminNotificationLogDto Log { get; private set; } = new();
    public List<SelectListItem> TripItems { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TripItems = (await service.GetNotificationTripOptionsAsync()).ToSelectListItems();
        if (ModelState.IsValid) Log = await service.GetNotificationsAsync(Filter);
    }

    public Dictionary<string, string> PageValues(int page) => new()
    {
        ["Filter.PageNumber"] = page.ToString(CultureInfo.InvariantCulture),
        ["Filter.TripId"] = Filter.TripId?.ToString(CultureInfo.InvariantCulture) ?? "",
        ["Filter.Passenger"] = Filter.Passenger ?? "",
        ["Filter.Type"] = Filter.Type?.ToString() ?? "",
        ["Filter.Channel"] = Filter.Channel?.ToString() ?? "",
        ["Filter.Status"] = Filter.Status?.ToString() ?? ""
    };
}
