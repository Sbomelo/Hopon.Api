using System.ComponentModel.DataAnnotations;
using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public partial class AdminMasterDataService(HoponDbContext db) : IAdminMasterDataService
{
    public Task<List<AdminBusDto>> GetBusesAsync() => BusQuery().OrderBy(b => b.RegistrationNumber).ToListAsync();
    public Task<AdminBusDto?> GetBusAsync(int id) => BusQuery().FirstOrDefaultAsync(b => b.Id == id);
    private IQueryable<AdminBusDto> BusQuery() => db.Buses.AsNoTracking().Select(b => new AdminBusDto
        { Id = b.Id, RegistrationNumber = b.RegistrationNumber, Model = b.Model, Capacity = b.Capacity });

    public async Task<AdminSaveResult> SaveBusAsync(int? id, BusInput input)
    {
        Validate(input);
        var registration = input.RegistrationNumber.Trim().ToUpperInvariant();
        if (await db.Buses.AnyAsync(b => b.Id != id && b.RegistrationNumber.Trim().ToUpper() == registration))
            return new(false, "This registration number is already in use.");
        var bus = id.HasValue ? await db.Buses.FindAsync(id.Value) : new Bus();
        if (bus is null) return new(false, "Bus not found.");
        if (id.HasValue && await db.Trips.AnyAsync(t => t.BusId == id && t.Tickets.Count(k => k.IsActive) > input.Capacity))
            return new(false, "Capacity cannot be lower than the active ticket count on a trip using this bus.");
        bus.RegistrationNumber = registration;
        bus.Model = input.Model?.Trim();
        bus.Capacity = input.Capacity;
        if (!id.HasValue) db.Buses.Add(bus);
        try { await db.SaveChangesAsync(); return new(true, Id: bus.Id); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "Could not save the bus. Its registration may already be in use; please retry."); }
    }

    public async Task<AdminSaveResult> DeleteBusAsync(int id)
    {
        var bus = await db.Buses.FindAsync(id);
        if (bus is null) return new(false, "Bus not found.");
        if (await db.Trips.AnyAsync(t => t.BusId == id)) return new(false, "This bus is referenced by a trip and cannot be deleted.");
        db.Buses.Remove(bus);
        try { await db.SaveChangesAsync(); return new(true); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "This bus is referenced by a trip and cannot be deleted."); }
    }

    public Task<List<AdminStopDto>> GetStopsAsync() => StopQuery().OrderBy(s => s.Name).ToListAsync();
    public Task<AdminStopDto?> GetStopAsync(int id) => StopQuery().FirstOrDefaultAsync(s => s.Id == id);
    private IQueryable<AdminStopDto> StopQuery() => db.Stops.AsNoTracking().Select(s => new AdminStopDto
        { Id = s.Id, Name = s.Name, Latitude = s.Latitude, Longitude = s.Longitude });

    public async Task<AdminSaveResult> SaveStopAsync(int? id, StopInput input)
    {
        Validate(input);
        if (!double.IsFinite(input.Latitude!.Value) || !double.IsFinite(input.Longitude!.Value))
            return new(false, "Coordinates must be finite numbers.");
        var stop = id.HasValue ? await db.Stops.FindAsync(id.Value) : new Stop();
        if (stop is null) return new(false, "Stop not found.");
        stop.Name = input.Name.Trim(); stop.Latitude = input.Latitude.Value; stop.Longitude = input.Longitude.Value;
        if (!id.HasValue) db.Stops.Add(stop);
        try { await db.SaveChangesAsync(); return new(true, Id: stop.Id); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "Could not save the stop. Please refresh and retry."); }
    }

    public async Task<AdminSaveResult> DeleteStopAsync(int id)
    {
        var stop = await db.Stops.FindAsync(id);
        if (stop is null) return new(false, "Stop not found.");
        if (await db.RouteStops.AnyAsync(s => s.StopId == id) || await db.TripStops.AnyAsync(s => s.StopId == id)
            || await db.BoardingLogs.AnyAsync(b => b.StopId == id))
            return new(false, "This stop is referenced by a route, trip or boarding log and cannot be deleted.");
        db.Stops.Remove(stop);
        try { await db.SaveChangesAsync(); return new(true); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "This stop is referenced by a route, trip or boarding log and cannot be deleted."); }
    }

    private static void Validate(object input) => Validator.ValidateObject(input, new ValidationContext(input), true);
}
