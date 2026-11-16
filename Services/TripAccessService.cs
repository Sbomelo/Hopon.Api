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
                                   .Include(t => t.DestinationTripStop).ThenInclude(ts => ts!.Stop)
                                   .FirstOrDefaultAsync(t => t.UserId == userId && t.TripId == tripId);

            if(ticket is null || !ticket.IsActive)
            {
                return new TripAccessResult { HasAccess = false, IsTrackingActive = false };
                
            }

            var trackingActive = TripStatusRules.IsTrackingActive(ticket.Trip.Status);
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
