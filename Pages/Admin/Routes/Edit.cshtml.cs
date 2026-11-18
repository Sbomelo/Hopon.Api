using Hopon.Api.DTOs.Admin;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hopon.Api.Pages.Admin.Routes;

public class EditModel(IAdminMasterDataService service) : PageModel
{
    public RouteInput Input { get; set; } = new();
    public AddRouteStopInput Addition { get; set; } = new();
    public AdminRouteDto? Route { get; private set; }
    public List<SelectListItem> StopItems { get; private set; } = new();
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue && !await LoadAsync(id.Value)) return NotFound();
        if (Route is not null) Input = new RouteInput { Name = Route.Name };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, RouteInput input)
    {
        Input = input;
        if (id.HasValue && !await LoadAsync(id.Value)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var result = await service.SaveRouteAsync(id, input);
        if (result.Success) return RedirectToPage(new { id = result.Id });
        ModelState.AddModelError(string.Empty, result.Error!);
        return Page();
    }

    public Task<IActionResult> OnPostAddAsync(int id, AddRouteStopInput addition)
        => ChangeAsync(id, RouteStopAction.Add, addition.StopId);
    public Task<IActionResult> OnPostRemoveAsync(int id, ChangeRouteStopInput change)
        => ChangeAsync(id, RouteStopAction.Remove, change.RouteStopId);
    public Task<IActionResult> OnPostMoveUpAsync(int id, ChangeRouteStopInput change)
        => ChangeAsync(id, RouteStopAction.MoveUp, change.RouteStopId);
    public Task<IActionResult> OnPostMoveDownAsync(int id, ChangeRouteStopInput change)
        => ChangeAsync(id, RouteStopAction.MoveDown, change.RouteStopId);

    private async Task<IActionResult> ChangeAsync(int id, RouteStopAction action, int itemId)
    {
        if (!await LoadAsync(id)) return NotFound();
        Input = new RouteInput { Name = Route!.Name };
        if (!ModelState.IsValid) return Page();
        var result = await service.ChangeRouteStopAsync(id, action, itemId);
        if (!result.Success) Error = result.Error;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        Route = await service.GetRouteAsync(id);
        if (Route is null) return false;
        var usedIds = Route.Stops.Select(s => s.StopId).ToHashSet();
        StopItems = (await service.GetStopsAsync()).Where(s => !usedIds.Contains(s.Id))
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
        return true;
    }
}
