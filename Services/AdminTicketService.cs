using System.Data;
using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class AdminTicketService(HoponDbContext db, TripTrackingSessions sessions, IHubContext<TripHub> hub) : IAdminTicketService
{
    public async Task<(bool Success, string? Error)> SetActiveAsync(int tripId, int ticketId, bool active)
    {
        int userId;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var ticket = await db.Tickets.Include(t => t.Trip).ThenInclude(t => t.Bus)
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.TripId == tripId);
            if (ticket is null) return (false, "Ticket not found on this trip.");
            if (ticket.Trip.Status != TripStatus.Scheduled)
                return (false, "Tickets can only be voided or reinstated while the trip is Scheduled.");
            if (active && !ticket.IsActive)
            {
                if (!await db.Users.AnyAsync(u => u.Id == ticket.UserId && u.IsActive))
                    return (false, "The passenger is inactive; the ticket cannot be reinstated.");
                if (ticket.Trip.Bus.Capacity is int capacity && await db.Tickets.CountAsync(t => t.TripId == tripId && t.IsActive) >= capacity)
                    return (false, "This bus is full. The ticket cannot be reinstated.");
                if (ticket.SeatNumber is not null && await db.Tickets.AnyAsync(t => t.TripId == tripId && t.IsActive && t.SeatNumber == ticket.SeatNumber))
                    return (false, $"Seat {ticket.SeatNumber} is already taken. The ticket cannot be reinstated.");
            }
            ticket.IsActive = active;
            userId = ticket.UserId;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return (false, "Could not change the ticket. Another administrator may have changed it; please retry.");
        }
        if (!active)
            await sessions.RevokeAsync(userId, tripId, connection => hub.Groups.RemoveFromGroupAsync(connection, TripHub.GroupName(tripId)));
        return (true, null);
    }
}
