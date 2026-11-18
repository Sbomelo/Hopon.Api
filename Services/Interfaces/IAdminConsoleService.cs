using Hopon.Api.DTOs.Admin;

namespace Hopon.Api.Services.Interfaces;

public interface IAdminConsoleService
{
    Task<AdminHomeDto> GetHomeAsync();
    Task<List<AdminMonitorDto>> GetMonitorAsync();
}
