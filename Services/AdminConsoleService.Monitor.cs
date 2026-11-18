using System.Globalization;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public partial class AdminConsoleService
{
    public async Task<List<AdminMonitorDto>> GetMonitorAsync()
    {
        var trips = await db.Trips.AsNoTracking()
            .Where(t => t.Status == TripStatus.InProgress || t.Status == TripStatus.Delayed)
            .Include(t => t.BusRoute).Include(t => t.Bus).Include(t => t.Driver)
            .OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.Id).ToListAsync();
        var tripIds = trips.Select(t => t.Id).ToList();
        var locations = await db.LocationUpdates.AsNoTracking()
            .Where(l => tripIds.Contains(l.TripId) && !db.LocationUpdates.Any(newer => newer.TripId == l.TripId
                && (newer.RecordedAt > l.RecordedAt || (newer.RecordedAt == l.RecordedAt && newer.Id > l.Id))))
            .ToDictionaryAsync(l => l.TripId);
        var stops = await db.TripStops.AsNoTracking().Where(s => tripIds.Contains(s.TripId))
            .Include(s => s.Stop).OrderBy(s => s.SequenceOrder).ToListAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        return trips.Select(trip =>
        {
            var fix = locations.GetValueOrDefault(trip.Id);
            var tripStops = stops.Where(s => s.TripId == trip.Id).ToList();
            var final = tripStops.LastOrDefault();
            var eta = final is null ? EtaCalculator.CalculateEta(trip).EstimatedArrival
                : EtaCalculator.CalculateStopEta(trip, final).EstimatedArrival;
            return new AdminMonitorDto
            {
                TripId = trip.Id, Route = trip.BusRoute.Name, Bus = trip.Bus.RegistrationNumber,
                Driver = trip.Driver?.FullName, Status = trip.Status.ToString(),
                Latitude = fix?.Latitude, Longitude = fix?.Longitude,
                AgeMinutes = fix is null ? null : Math.Max(0, (now - fix.RecordedAt).TotalMinutes),
                LocationUrl = fix is null ? null : string.Create(CultureInfo.InvariantCulture,
                    $"https://www.openstreetmap.org/?mlat={fix.Latitude}&mlon={fix.Longitude}#map=15/{fix.Latitude}/{fix.Longitude}"),
                NextStop = tripStops.FirstOrDefault(s => s.Status == TripStopStatus.Pending)?.Stop.Name,
                FinalEtaSast = eta.HasValue ? SaTime.ToSa(eta.Value).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : null
            };
        }).ToList();
    }
}
