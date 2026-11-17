using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Hopon.Api.Services.Interfaces;

namespace Hopon.Api.Services.Notifications;

public sealed class NotificationSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationSchedulerService> _logger;
    private readonly int _clockOffsetMinutes;

    public NotificationSchedulerService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
        IConfiguration configuration, IHostEnvironment environment, ILogger<NotificationSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _clockOffsetMinutes = environment.IsDevelopment()
            ? configuration.GetValue<int>("Demo:ClockOffsetMinutes", 0) : 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Notification scheduler tick failed; the next tick will retry.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown while waiting for a tick.
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_clockOffsetMinutes);

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.IsActive && t.DestinationTripStop != null
                && t.DestinationTripStop.TripId == t.TripId
                && t.DestinationTripStop.Status == TripStopStatus.Pending
                && (t.Trip.Status == TripStatus.InProgress || t.Trip.Status == TripStatus.Delayed)
                && !t.BoardingLogs.Any(b => b.EventType == BoardingEventType.Alighted))
            .Include(t => t.Trip)
            .Include(t => t.DestinationTripStop).ThenInclude(s => s!.Stop)
            .ToListAsync(cancellationToken);

        if (tickets.Count == 0)
            return;

        var tripIds = tickets.Select(t => t.TripId).Distinct().ToList();
        var userIds = tickets.Select(t => t.UserId).Distinct().ToList();
        var existing = await db.NotificationLogs.AsNoTracking()
            .Where(n => n.TripId.HasValue && tripIds.Contains(n.TripId.Value) && userIds.Contains(n.UserId))
            .Select(n => new { n.TripId, n.UserId, n.Type })
            .ToListAsync(cancellationToken);
        var sentByPassenger = existing.GroupBy(n => (n.TripId!.Value, n.UserId))
            .ToDictionary(g => g.Key, g => g.Select(n => n.Type).ToHashSet());

        foreach (var ticket in tickets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stop = ticket.DestinationTripStop!;
                var (eta, isEstimate) = EtaCalculator.CalculateStopEta(ticket.Trip, stop);
                if (!eta.HasValue || !isEstimate)
                    continue;

                var sent = sentByPassenger.GetValueOrDefault((ticket.TripId, ticket.UserId)) ?? new HashSet<NotificationType>();
                var due = NotificationThresholds.GetDue(now, eta.Value, sent);
                if (!due.HasValue || sent.Contains(due.Value))
                    continue;

                await notifications.CreateAsync(ticket.UserId, ticket.TripId, due.Value,
                    NotificationMessages.Reminder(due.Value, stop.Stop.Name, eta.Value),
                    [NotificationChannel.InApp, NotificationChannel.Sms]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not process notification for ticket {TicketId}; continuing with the next ticket.", ticket.Id);
            }
        }
    }
}
