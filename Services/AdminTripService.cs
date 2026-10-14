using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Hopon.Api.Services
{
    public class AdminTripService : IAdminTripService
    {

        private const string ReferenceAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        private const int ReferenceLength = 6;
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
                ActualDeparture = trip.ActualDeparture,
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

        public async Task<(bool Success, string? Error, string? TicketReference)> IssueTicketAsync(int tripId, int userId, string? seatNumber)
        {
            var trip = await _db.Trips
                                .Include(t => t.Bus)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip is null)
                return (false, "Trip not found.", null);

            if (trip.Status is TripStatus.Completed or TripStatus.Cancelled)
                return (false, $"Tickets cannot be issued for a {trip.Status} trip.", null);

            var passengerExists = await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive);
            if (!passengerExists)
                return (false, "Passenger not found.", null);

            // Includes inactive tickets: the (UserId, TripId) unique index covers them too
            var alreadyTicketed = await _db.Tickets.AnyAsync(t => t.TripId == tripId && t.UserId == userId);
            if (alreadyTicketed)
                return (false, "This passenger already has a ticket for this trip.", null);

            if (trip.Bus.Capacity is int capacity)
            {
                var sold = await _db.Tickets.CountAsync(t => t.TripId == tripId && t.IsActive);
                if (sold >= capacity)
                    return (false, "This bus is full.", null);
            }

            var seat = string.IsNullOrWhiteSpace(seatNumber) ? null : seatNumber.Trim().ToUpperInvariant();

            if (seat is not null)
            {
                var seatTaken = await _db.Tickets.AnyAsync(t => t.TripId == tripId && t.IsActive && t.SeatNumber == seat);
                if (seatTaken)
                    return (false, $"Seat {seat} is already taken on this trip.", null);
            }

            var reference = await GenerateUniqueTicketReferenceAsync();

            _db.Tickets.Add(new Ticket
            {
                TicketReference = reference,
                UserId = userId,
                TripId = tripId,
                SeatNumber = seat
            });

            await _db.SaveChangesAsync();

            return (true, null, reference);
        }

        public async Task<List<SelectOptionDto>> GetAvailablePassengerOptionsAsync(int tripId)
        {
            return await _db.Users
                            .Where(u => u.IsActive &&
                                        !_db.Tickets.Any(t => t.TripId == tripId && t.UserId == u.Id))
                            .AsNoTracking()
                            .OrderBy(u => u.FullName)
                            .Select(u => new SelectOptionDto
                            {
                                Id = u.Id,
                                Label = u.FullName + " (" + u.PhoneNumber + ")"
                            })
                            .ToListAsync();
        }

        private async Task<string> GenerateUniqueTicketReferenceAsync()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var chars = new char[ReferenceLength];
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];

                var reference = $"HOP-{new string(chars)}";

                if (!await _db.Tickets.AnyAsync(t => t.TicketReference == reference))
                    return reference;
            }

            throw new InvalidOperationException("Could not generate a unique ticket reference.");
        }

    }
}
