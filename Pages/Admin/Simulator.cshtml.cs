using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hopon.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SimulatorModel(SimulatorService simulator) : PageModel
{
    public List<SimulatorChoice> Choices { get; private set; } = [];
    public SimulatorSnapshot? Trip { get; private set; }
    public string? Feedback { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? tripId)
    {
        Choices = await simulator.ChoicesAsync();
        var id = tripId ?? Choices.FirstOrDefault()?.Id;
        if (id.HasValue)
        {
            Trip = await simulator.SnapshotAsync(id.Value);
            if (Trip == null) return NotFound();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostActionAsync(int tripId, string command, string? reason)
    {
        var result = await simulator.ActAsync(tripId, command, reason);
        Trip = await simulator.SnapshotAsync(tripId);
        if (Trip == null) return NotFound();
        Feedback = result.Error;
        if (Request.Headers["X-Simulator-Panel"] == "1") return Partial("_SimulatorPanel", this);
        Choices = await simulator.ChoicesAsync();
        return Page();
    }
}
