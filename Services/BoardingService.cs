using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class BoardingService: IBoardingService
    {
        private readonly HoponDbContext _db;

        public BoardingService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<BoardingStatusDto> GetStatusAsync(int ticketId, int tripId)
        {
            var logs = await _db.BoardingLogs
                                .Where(bl => bl.TicketId == ticketId)
                                .AsNoTracking()
                                .ToListAsync();

            return BuildStatus(tripId, logs);
        }

        public async Task<(bool Success, string? Error, BoardingStatusDto? Status)> ConfirmBoardingAsync(int ticketId, int tripId, TripStatus tripStatus)
        {
            if (tripStatus == TripStatus.Cancelled)
                return (false, "This trip has been canceled.", null);

            var logs = await _db.BoardingLogs
                                .Where(bl => bl.TicketId == ticketId)
                                .AsNoTracking()
                                .ToListAsync();

            if (logs.Any(l => l.EventType == BoardingEventType.Boarded))
                return (false, "You have already confirmed boarding for this trip.", null);

            var log = new BoardingLog
            {
                TicketId = ticketId,
                EventType = BoardingEventType.Boarded,
                Timestamp = DateTime.UtcNow
            };

            _db.BoardingLogs.Add(log);
            await _db.SaveChangesAsync();

            logs.Add(log);
            return (true, null, BuildStatus(tripId, logs));
        }

        public async Task<(bool Success, string? Error, BoardingStatusDto? Status)> ConfirmAlightingAsync(int ticketId, int tripId, TripStatus tripStatus)
        {
            if (tripStatus == TripStatus.Cancelled)
                return (false, "This trip has been cancelled", null);

            var logs = await _db.BoardingLogs
                                .Where(bl => bl.TicketId == ticketId)
                                .AsNoTracking()
                                .ToListAsync();

            if (!logs.Any(l => l.EventType == BoardingEventType.Boarded))
                return (false, "Confirm boarding before confirming you have left the bus.", null);

            if (logs.Any(l => l.EventType == BoardingEventType.Alighted))
                return (false, "You have already confirmed leaving the bus for this trip.", null);

            var log = new BoardingLog
            {
                TicketId = ticketId,
                EventType = BoardingEventType.Alighted,
                Timestamp = DateTime.UtcNow
            };

            _db.BoardingLogs.Add(log);
            await _db.SaveChangesAsync();

            logs.Add(log);
            return (true, null, BuildStatus(tripId, logs));
        }

        private static BoardingStatusDto BuildStatus(int tripId, List<BoardingLog> logs)
        {
            var boarded = logs.Where(l => l.EventType == BoardingEventType.Boarded)
                              .OrderBy(l => l.Timestamp)
                              .FirstOrDefault();

            var alighted = logs.Where(l => l.EventType == BoardingEventType.Alighted)
                               .OrderBy(l => l.Timestamp)
                               .FirstOrDefault();

            return new BoardingStatusDto
            {
                TripId = tripId,
                HasBoarded = boarded is not null,
                BoardedAt = boarded?.Timestamp,
                HasAlighted = alighted is not null,
                AlightedAt = alighted?.Timestamp
            };
        }
    }
}
