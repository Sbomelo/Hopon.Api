using Hopon.Api.Data;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class TripAccessService : ITripAccessService
    {
        private readonly HoponDbContext _db;

        public TripAccessService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<TripAccessResult> CheckTripAccessAsync(int userId, int tripId)
        {
            var ticket = await _db.Tickets
                                   .Include(t => t.Trip)
                                   .FirstOrDefaultAsync(t => t.UserId == userId && t.TripId == tripId);

            if(ticket is null || !ticket.IsActive)
            {
                return new TripAccessResult { HasAccess = false, IsTrackingActive = false };
                
            }

            var trackingActive = ticket.Trip.Status
                    is TripStatus.Scheduled or TripStatus.InProgress or TripStatus.Delayed;
            return new TripAccessResult
            {
                HasAccess = true,
                IsTrackingActive = trackingActive,
                Ticket = ticket,
                Trip = ticket.Trip
            };
        }


    }
}
