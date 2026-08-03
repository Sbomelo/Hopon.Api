using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class AdminTripService : IAdminTripService
    {
        private readonly HoponDbContext _db;

        public AdminTripService(HoponDbContext db)
        {
            _db = db;

        }

        public async Task<List<AdminTripListItemDto>> GetTripsAsync(TripStatus? statusFilter, int? routeIdFilter)
        {
            var query = _db.Trips
                .Include(t => t.BusRoute)
                .Include(t => t.Bus)
                .Include(t => t.Driver)
                .AsNoTracking()
                .AsQueryable();

            if (statusFilter.HasValue)
                query = query.Where(t => t.Status == statusFilter.Value);

            if (routeIdFilter.HasValue)
                query = query.Where(t => t.BusRouteId == routeIdFilter.Value);

            var trips = await query
                            .OrderByDescending(t => t.ScheduledDeparture)
                            .ToListAsync();

            var tripIds = trips.Select(t => t.Id)
                               .ToList();

            var ticketCounts = await _db.Tickets
                              .Where(tk => tripIds.Contains(tk.TripId) && tk.IsActive)
                              .GroupBy(tk => tk.TripId)
                              .Select(g => new { TripId = g.Key, Count = g.Count() })
                              .ToDictionaryAsync(g => g.TripId, g => g.Count);

            return trips.Select(t => new AdminTripListItemDto
            {

                TripId = t.Id,
                RouteName = t.BusRoute.Name,
                BusRegistration = t.Bus.RegistrationNumber,
                DriverName = t.Driver?.FullName,
                ScheduledDeparture = t.ScheduledDeparture,
                ScheduledArrival = t.ScheduledArrival,
                Status = t.Status.ToString(),
                TicketsSold = ticketCounts.TryGetValue(t.Id, out var count) ? count : 0
            }).ToList();
        }

        public async Task<AdminTripDetailDto?> GetTripDetailAsync (int tripId)
        {
            var trip = await _db.Trips
                                .Include(t => t.BusRoute)
                                .Include(t => t.Bus)
                                .Include(t => t.Driver)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return null;

            var tickets = await _db.Tickets
                                .Where(tk => tk.TripId == tripId)
                                .Include(tk => tk.User)
                                .AsNoTracking()
                                .ToListAsync();
            return new AdminTripDetailDto
            {
                TripId = trip.Id,
                BusRouteId = trip.BusRouteId,
                RouteName = trip.BusRoute.Name,
                BusId = trip.BusId,
                BusRegistration = trip.Bus.RegistrationNumber,
                DriverId = trip.DriverId,
                DriverName = trip.Driver?.FullName,
                ScheduledArrival = trip.ScheduledArrival,
                ScheduledDeparture = trip.ScheduledDeparture,
                ActualArrival = trip.ActualArrival,
                Status = trip.Status.ToString(),
                Tickets = tickets.Select(tk => new AdminTicketSummaryDto
                {
                    TicketReference = tk.TicketReference,
                    PassengerName = tk.User.FullName,
                    SeatNumber = tk.SeatNumber,
                    IsActive = tk.IsActive
                }).ToList()
            };

        }

        public async Task<int> CreateTripAsync(int busRouteId, int busId, int? driverId, DateTime scheduledDeparture, DateTime scheduledArrival)
        {
            var trip = new Trip
            {
                BusRouteId = busRouteId,
                BusId = busId,
                DriverId = driverId,
                ScheduledDeparture = scheduledDeparture,
                ScheduledArrival = scheduledArrival,
                Status = TripStatus.Scheduled
            };

            _db.Trips.Add(trip);
            await _db.SaveChangesAsync();

            return trip.Id;
        }

        public async Task<bool> UpdateTripAsync(int tripId, int busRouteId, int busId, int? driverId, DateTime scheduledDeparture, DateTime scheduledArrival)
        {
            var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return false;

            trip.BusRouteId = busRouteId;
            trip.BusId = busId;
            trip.DriverId = driverId;
            trip.ScheduledDeparture = scheduledDeparture;
            trip.ScheduledArrival = scheduledArrival;

            await _db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> UpdateTripStatusAsync(int tripId, TripStatus newStatus)
        {
            var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return false;

            trip.Status = newStatus;

            await _db.SaveChangesAsync();
            return true;

        }

        public async Task<List<SelectOptionDto>> GetRouteOptionsAsync()
        {
            return await _db.BusRoutes
                            .AsNoTracking()
                            .OrderBy(r => r.Name)
                            .Select(r => new SelectOptionDto { Id = r.Id, Label = r.Name })
                            .ToListAsync();
        }

        public async Task<List<SelectOptionDto>> GetBusOptionsAsync()
        {
            return await _db.Buses
                            .AsNoTracking()
                            .OrderBy(b => b.RegistrationNumber)
                            .Select(b => new SelectOptionDto { Id = b.Id, Label = b.RegistrationNumber })
                            .ToListAsync();
        }

        public async Task<List<SelectOptionDto>> GetDriverOptionsAsync()
        {
            return await _db.Drivers
                            .Where(d => d.IsActive)
                            .AsNoTracking()
                            .OrderBy(d => d.FullName)
                            .Select(d => new SelectOptionDto{ Id = d.Id, Label = d.FullName })
                            .ToListAsync();
        }
    }
}
