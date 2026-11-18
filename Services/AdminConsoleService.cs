using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public partial class AdminConsoleService(HoponDbContext db, TimeProvider clock) : IAdminConsoleService
{
    public async Task<AdminHomeDto> GetHomeAsync()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = SaTime.ToUtc(SaTime.ToSa(now).Date);
        var tomorrow = today.AddDays(1);
        var yesterday = now.AddHours(-24);
        return new AdminHomeDto
        {
            InProgress = await db.Trips.CountAsync(t => t.Status == TripStatus.InProgress),
            Delayed = await db.Trips.CountAsync(t => t.Status == TripStatus.Delayed),
            ScheduledToday = await db.Trips.CountAsync(t => t.Status == TripStatus.Scheduled
                && t.ScheduledDeparture >= today && t.ScheduledDeparture < tomorrow),
            TicketsSoldToday = await db.Tickets.CountAsync(t => t.PurchaseDate >= today && t.PurchaseDate < tomorrow),
            NotificationsSent = await db.NotificationLogs.CountAsync(n => n.CreatedAt >= yesterday
                && n.CreatedAt <= now && n.Status == NotificationStatus.Sent),
            NotificationsFailed = await db.NotificationLogs.CountAsync(n => n.CreatedAt >= yesterday
                && n.CreatedAt <= now && n.Status == NotificationStatus.Failed)
        };
    }
}
