using System.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public partial class AdminMasterDataService
{
    public Task<List<AdminRouteDto>> GetRoutesAsync() => RouteQuery().OrderBy(r => r.Name).ToListAsync();
    public Task<AdminRouteDto?> GetRouteAsync(int id) => RouteQuery().FirstOrDefaultAsync(r => r.Id == id);
    private IQueryable<AdminRouteDto> RouteQuery() => db.BusRoutes.AsNoTracking().Select(r => new AdminRouteDto
    {
        Id = r.Id, Name = r.Name, UsedByTrips = r.Trips.Any(),
        Stops = r.RouteStops.OrderBy(s => s.SequenceOrder).Select(s => new AdminRouteStopDto
            { Id = s.Id, StopId = s.StopId, Name = s.Stop.Name, SequenceOrder = s.SequenceOrder }).ToList()
    });

    public async Task<AdminSaveResult> SaveRouteAsync(int? id, RouteInput input)
    {
        Validate(input);
        var route = id.HasValue ? await db.BusRoutes.FindAsync(id.Value) : new BusRoute();
        if (route is null) return new(false, "Route not found.");
        route.Name = input.Name.Trim();
        if (!id.HasValue) db.BusRoutes.Add(route);
        try { await db.SaveChangesAsync(); return new(true, Id: route.Id); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "Could not save the route. Please refresh and retry."); }
    }

    public async Task<AdminSaveResult> DeleteRouteAsync(int id)
    {
        var route = await db.BusRoutes.FindAsync(id);
        if (route is null) return new(false, "Route not found.");
        if (await db.Trips.AnyAsync(t => t.BusRouteId == id)) return new(false, "This route is referenced by a trip and cannot be deleted.");
        db.BusRoutes.Remove(route);
        try { await db.SaveChangesAsync(); return new(true); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(false, "This route is referenced by a trip and cannot be deleted."); }
    }

    public async Task<AdminSaveResult> ChangeRouteStopAsync(int routeId, RouteStopAction action, int itemId)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if (!await db.BusRoutes.AnyAsync(r => r.Id == routeId)) return new(false, "Route not found.");
            if (action != RouteStopAction.Add && await db.Trips.AnyAsync(t => t.BusRouteId == routeId))
                return new(false, "This route is used by a trip. Stops cannot be reordered or removed because trip schedules and passenger destinations rely on that order. Create a new route instead.");
            var stops = await db.RouteStops.Where(s => s.BusRouteId == routeId).OrderBy(s => s.SequenceOrder).ToListAsync();
            if (action == RouteStopAction.Add)
            {
                if (!await db.Stops.AnyAsync(s => s.Id == itemId)) return new(false, "Select an existing stop.");
                if (stops.Any(s => s.StopId == itemId)) return new(false, "This stop is already on the route.");
                var added = new RouteStop { BusRouteId = routeId, StopId = itemId };
                db.RouteStops.Add(added);
                stops.Add(added);
            }
            else
            {
                var index = stops.FindIndex(s => s.Id == itemId);
                if (index < 0) return new(false, "Stop not found on this route.");
                if (action == RouteStopAction.Remove) { db.RouteStops.Remove(stops[index]); stops.RemoveAt(index); }
                else if (action is RouteStopAction.MoveUp or RouteStopAction.MoveDown)
                {
                    var target = index + (action == RouteStopAction.MoveUp ? -1 : 1);
                    if (target < 0 || target >= stops.Count) return new(false, "This stop is already at the end of the route.");
                    (stops[index], stops[target]) = (stops[target], stops[index]);
                }
                else return new(false, "Unknown stop action.");
            }
            // Two saves inside one transaction avoid collisions with the unique route/sequence index during swaps.
            var temporaryStart = Math.Min(0, stops.Select(s => s.SequenceOrder).DefaultIfEmpty(0).Min()) - stops.Count - 1;
            for (var i = 0; i < stops.Count; i++) stops[i].SequenceOrder = temporaryStart + i;
            await db.SaveChangesAsync();
            for (var i = 0; i < stops.Count; i++) stops[i].SequenceOrder = i + 1;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new(true, Id: routeId);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new(false, "Could not update route stops. Another administrator may have changed the route; refresh and retry.");
        }
    }
}
