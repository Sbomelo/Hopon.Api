using System.ComponentModel.DataAnnotations;
using Hopon.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public partial class AdminConsoleService
{
    public async Task<AdminNotificationLogDto> GetNotificationsAsync(NotificationLogFilter filter)
    {
        Validator.ValidateObject(filter, new ValidationContext(filter), true);
        var query = db.NotificationLogs.AsNoTracking();
        if (filter.TripId.HasValue) query = query.Where(n => n.TripId == filter.TripId.Value);
        var passenger = filter.Passenger?.Trim();
        if (!string.IsNullOrEmpty(passenger))
            query = query.Where(n => n.User.FullName.Contains(passenger) || n.User.PhoneNumber.Contains(passenger));
        if (filter.Type.HasValue) query = query.Where(n => n.Type == filter.Type.Value);
        if (filter.Channel.HasValue) query = query.Where(n => n.Channel == filter.Channel.Value);
        if (filter.Status.HasValue) query = query.Where(n => n.Status == filter.Status.Value);
        var result = new AdminNotificationLogDto { Total = await query.CountAsync() };
        result.PageNumber = Math.Min(filter.PageNumber, result.TotalPages);
        result.Items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((result.PageNumber - 1) * AdminNotificationLogDto.PageSize).Take(AdminNotificationLogDto.PageSize)
            .Select(n => new AdminNotificationRowDto
            {
                Id = n.Id, Timestamp = n.CreatedAt, Passenger = n.User.FullName,
                Route = n.Trip == null ? null : n.Trip.BusRoute.Name,
                Type = n.Type, Channel = n.Channel, Status = n.Status, Message = n.Message
            }).ToListAsync();
        return result;
    }

    public async Task<List<SelectOptionDto>> GetNotificationTripOptionsAsync()
    {
        var trips = await db.Trips.AsNoTracking().OrderByDescending(t => t.ScheduledDeparture)
            .Select(t => new { t.Id, t.BusRoute.Name, t.ScheduledDeparture }).ToListAsync();
        return trips.Select(t => new SelectOptionDto { Id = t.Id,
            Label = $"#{t.Id} · {t.Name} · {SaTime.ToSa(t.ScheduledDeparture):dd MMM yyyy HH:mm} SAST" }).ToList();
    }
}
