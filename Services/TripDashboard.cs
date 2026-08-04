using Hopon.Api.Data;
using Hopon.Api.DTOs.Trips;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class TripDashboardService : ITripDashboardService
    {
        private readonly HoponDbContext _context;

        public TripDashboardService(HoponDbContext context)
        {
            _context = context;
        }


        public async Task<MyTripsResponseDto> GetMyTripsAsync(int userId)
        {
            var tickets = await _context.Tickets
                                        .Where(t => t.UserId == userId && t.IsActive)
                                        .Include(t => t.Trip)
                                            .ThenInclude(trip => trip.BusRoute)
                                        .AsNoTracking()
                                        .ToListAsync();

            var response = new MyTripsResponseDto();

            foreach (var ticket in tickets)
            {
                var trip = ticket.Trip;

                var (estimatedArrival, isEstimate) = EtaCalculator.CalculateEta(trip);

                var dto = new MyTripDto
                {
                    TripId = trip.Id,
                    TicketReference = ticket.TicketReference,
                    RouteName = trip.BusRoute.Name,
                    TripDate = trip.ScheduledDeparture.Date,
                    ScheduledDeparture = trip.ScheduledDeparture,
                    ScheduledArrival = trip.ScheduledArrival,
                    ActualDepature = trip.ActualDeparture,
                    ActualArrival = trip.ActualArrival,
                    EstimatedArrival = estimatedArrival,
                    IsEstimate = isEstimate,
                    Status = trip.Status.ToString(),
                    IsLive = TripStatusRules.IsTrackingActive(trip.Status)
                };

                if (dto.IsLive)
                    response.LiveTrips.Add(dto);

                else
                    response.PastTrips.Add(dto);

            }

            response.LiveTrips = response.LiveTrips
                                         .OrderBy(t => t.ScheduledArrival)
                                         .ToList();

            response.PastTrips = response.PastTrips
                                         .OrderByDescending(t => t.ScheduledArrival)
                                         .ToList();

            return response;
        }
    }
    
}
