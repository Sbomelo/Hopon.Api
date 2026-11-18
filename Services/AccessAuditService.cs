using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public sealed class AccessAuditService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AccessAuditService> logger)
{
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task RecordAsync(int userId, int? tripId, AccessAction action)
    {
        var gate = _gates[(int)((uint)userId % (uint)_gates.Length)];
        await gate.WaitAsync();
        try
        {
            // Separate context: an audit failure cannot poison the caller's tracked changes.
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            var cutoff = now.AddMinutes(-5);
            if (await db.AccessAuditLogs.AnyAsync(x => x.UserId == userId && x.TripId == tripId && x.Action == action && x.CreatedAt > cutoff)) return;
            db.AccessAuditLogs.Add(new AccessAuditLog { UserId = userId, TripId = tripId, Action = action, CreatedAt = now });
            await db.SaveChangesAsync();
        }
        catch (Exception) { logger.LogWarning("Could not write an access audit entry."); }
        finally { gate.Release(); }
    }
}
