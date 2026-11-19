using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuditModel(HoponDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TripId { get; set; }
    [BindProperty(SupportsGet = true)] public AccessAction? ActionFilter { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public List<AccessAuditLog> Items { get; private set; } = new();
    public async Task OnGetAsync()
    {
        if (!ModelState.IsValid) return;
        var query = db.AccessAuditLogs.AsNoTracking();
        if (UserId.HasValue) query = query.Where(x => x.UserId == UserId);
        if (TripId.HasValue) query = query.Where(x => x.TripId == TripId);
        if (ActionFilter.HasValue) query = query.Where(x => x.Action == ActionFilter);
        TotalPages = Math.Max(1, (int)Math.Ceiling(await query.CountAsync() / 50d));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((PageNumber - 1) * 50).Take(50).ToListAsync();
    }
}
