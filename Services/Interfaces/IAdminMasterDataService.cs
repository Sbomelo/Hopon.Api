using Hopon.Api.DTOs.Admin;

namespace Hopon.Api.Services.Interfaces;

public interface IAdminMasterDataService
{
    Task<List<AdminBusDto>> GetBusesAsync();
    Task<AdminBusDto?> GetBusAsync(int id);
    Task<AdminSaveResult> SaveBusAsync(int? id, BusInput input);
    Task<AdminSaveResult> DeleteBusAsync(int id);
    Task<List<AdminStopDto>> GetStopsAsync();
    Task<AdminStopDto?> GetStopAsync(int id);
    Task<AdminSaveResult> SaveStopAsync(int? id, StopInput input);
    Task<AdminSaveResult> DeleteStopAsync(int id);
    Task<List<AdminRouteDto>> GetRoutesAsync();
    Task<AdminRouteDto?> GetRouteAsync(int id);
    Task<AdminSaveResult> SaveRouteAsync(int? id, RouteInput input);
    Task<AdminSaveResult> DeleteRouteAsync(int id);
    Task<AdminSaveResult> ChangeRouteStopAsync(int routeId, RouteStopAction action, int itemId);
}
