using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services.Notifications;

public sealed class PassengerNotificationWriter
{
    private readonly HoponDbContext _db;
    private readonly ILogger<PassengerNotificationWriter> _logger;

    public PassengerNotificationWriter(HoponDbContext db, ILogger<PassengerNotificationWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryWriteAsync(NotificationLog log, CancellationToken cancellationToken = default)
    {
        // Callers check a batch of existing keys first; the unique index closes the race.
        _db.NotificationLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
        {
            // A failed insert must not be retried along with the next passenger's insert.
            _db.Entry(log).State = EntityState.Detached;
            _logger.LogWarning(exception, "Could not save {Type} notification for user {UserId}, trip {TripId}; it may already exist.",
                log.Type, log.UserId, log.TripId);
            return false;
        }
    }
}
