using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Hopon.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RetentionModel(RetentionRunner runner, IOptions<RetentionOptions> options, ILogger<RetentionModel> logger) : PageModel
{
    public RetentionSummary? Summary => runner.LastRun;
    public RetentionOptions Settings => options.Value;
    [TempData] public string? Message { get; set; }
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await runner.RunAsync(forceDryRun: true, cancellationToken: HttpContext.RequestAborted);
            Message = Settings.Enabled ? "Dry run completed. No data was changed." : "Retention is disabled. No data was changed.";
        }
        catch (Exception) { logger.LogWarning("Admin retention dry run failed."); Message = "Dry run failed. Please retry."; }
        return RedirectToPage();
    }
}
