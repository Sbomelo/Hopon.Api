using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace Hopon.Api.Services
{
    public class TripHistoryService : ITripHistoryService
    {
        private readonly HoponDbContext _db;
        public TripHistoryService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<TripHistoryDto?> GetTripHistoryAsync(int userId, int tripId)
        {
            var ticket = await _db.Tickets
                       .AsNoTracking()
                       .FirstOrDefaultAsync(tk => tk.UserId == userId && tk.TripId == tripId);

            if (ticket is null)
                return null;

            var trip = await _db.Trips
                        .Include(t => t.BusRoute)
                        .Include(t => t.TripStops.OrderBy(ts => ts.SequenceOrder))
                            .ThenInclude(ts => ts.Stop)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return null;

            var boardingLogs = await _db.BoardingLogs
                               .Where(bl => bl.TicketId == ticket.Id)
                               .AsNoTracking()
                               .ToListAsync();

            var boarded = boardingLogs
                            .Where(bl => bl.EventType == BoardingEventType.Boarded)
                            .OrderBy(bl => bl.Timestamp)
                            .FirstOrDefault();

            var alighted = boardingLogs
                              .Where(bl => bl.EventType == BoardingEventType.Alighted)
                              .OrderBy(bl => bl.Timestamp)
                              .FirstOrDefault();

            return new TripHistoryDto
            {
                TripId = trip.Id,
                TicketReference = ticket.TicketReference,
                SeatNumber = ticket.SeatNumber,
                RouteName = trip.BusRoute.Name,
                Status = trip.Status.ToString(),
                ScheduledDeparture = trip.ScheduledDeparture,
                ScheduledArrival = trip.ScheduledArrival,
                ActualDeparture = trip.ActualDeparture,
                ActualArrival = trip.ActualArrival,
                HasBoarded = boarded is not null,
                BoardedAt = boarded?.Timestamp,
                HasAlighted = alighted is not null,
                AlightedAt = alighted?.Timestamp,
                Stops = trip.TripStops.Select(ts => new TripHistoryStopDto
                {
                    SequenceOrder = ts.SequenceOrder,
                    StopName = ts.Stop.Name,
                    ScheduledArrival = ts.ScheduledArrival,
                    ScheduledDeparture = ts.ScheduledDeparture,
                    ActualArrival = ts.ActualArrival,
                    ActualDeparture = ts.ActualDeparture,
                    Status = ts.Status.ToString()
                }).ToList()
            };
        }
    }
}
