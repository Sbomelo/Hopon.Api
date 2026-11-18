using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hopon.Api.Services;

public sealed class RetentionRunner(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<RetentionOptions> options, ILogger<RetentionRunner> logger)
{
    private readonly SemaphoreSlim _gate = new(1);
    public RetentionSummary? LastRun { get; private set; }

    public async Task<RetentionSummary> RunAsync(bool forceDryRun = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = options.Value;
            var now = clock.GetUtcNow().UtcDateTime;
            var dry = forceDryRun || settings.DryRun;
            if (!settings.Enabled) return LastRun = new(now, dry, false);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var cutoff = now.AddDays(-settings.DaysAfterTrip);
            var locationCutoff = now.AddDays(-settings.LocationDays);
            var otpCutoff = now.AddDays(-2);
            // Cancelled trips lack an actual arrival; use their scheduled arrival conservatively.
            var expiredTrips = db.Trips.Where(t => (t.Status == TripStatus.Completed || t.Status == TripStatus.Cancelled)
                && (t.ActualArrival ?? t.ScheduledArrival) < cutoff).Select(t => t.Id);
            var expiredUsers = db.Users.Where(u => u.CreatedAt < cutoff && !u.PhoneNumber.StartsWith("deleted-") &&
                !u.Tickets.Any(t => (t.Trip.ActualArrival ?? t.Trip.ScheduledArrival) >= cutoff ||
                    (t.Trip.Status != TripStatus.Completed && t.Trip.Status != TripStatus.Cancelled)));
            var locations = await DeleteAsync(db.LocationUpdates.Where(x => x.RecordedAt < locationCutoff), dry, cancellationToken);
            var boardings = await DeleteAsync(db.BoardingLogs.Where(x => expiredTrips.Contains(x.Ticket.TripId)), dry, cancellationToken);
            var notifications = await DeleteAsync(db.NotificationLogs.Where(x => x.TripId.HasValue && expiredTrips.Contains(x.TripId.Value)), dry, cancellationToken);
            var audits = await DeleteAsync(db.AccessAuditLogs.Where(x => (x.TripId.HasValue && expiredTrips.Contains(x.TripId.Value)) || x.CreatedAt < cutoff), dry, cancellationToken);
            var otps = await DeleteAsync(db.OtpRequests.Where(x => x.CreatedAt < otpCutoff), dry, cancellationToken);
            var users = 0;
            var contacts = 0;
            if (dry)
            {
                users = await expiredUsers.CountAsync(cancellationToken);
                contacts = await db.EmergencyContacts.CountAsync(x => expiredUsers.Select(u => u.Id).Contains(x.UserId), cancellationToken);
            }
            else
            {
                while (true)
                {
                    var ids = await expiredUsers.OrderBy(x => x.Id).Select(x => x.Id).Take(500).ToListAsync(cancellationToken);
                    if (ids.Count == 0) break;
                    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
                    // Recheck eligibility in the transaction before removing contact data.
                    var eligible = await expiredUsers.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
                    contacts += await db.EmergencyContacts.Where(x => eligible.Contains(x.UserId)).ExecuteDeleteAsync(cancellationToken);
                    foreach (var id in eligible)
                    {
                        var phone = $"deleted-{id}-{Guid.NewGuid():N}";
                        users += await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.FullName, "Deleted user").SetProperty(x => x.PhoneNumber, phone)
                            .SetProperty(x => x.IsActive, false), cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            var summary = new RetentionSummary(now, dry, true, locations, boardings, notifications, audits, otps, users, contacts);
            LastRun = summary;
            logger.LogInformation("Retention dry run {DryRun}: locations {Locations}, boardings {Boardings}, notifications {Notifications}, audits {Audits}, OTPs {Otps}, users {Users}, contacts {Contacts}",
                dry, locations, boardings, notifications, audits, otps, users, contacts);
            return summary;
        }
        finally { _gate.Release(); }
    }

    private static async Task<int> DeleteAsync<T>(IQueryable<T> query, bool dry, CancellationToken token) where T : class
    {
        if (dry) return await query.CountAsync(token);
        var total = 0;
        while (true)
        {
            var count = await query.OrderBy(x => EF.Property<int>(x, "Id")).Take(500).ExecuteDeleteAsync(token);
            total += count;
            if (count < 500) return total;
        }
    }
}
