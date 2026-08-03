using Hopon.Api.Models;

namespace Hopon.Api.Services.Interfaces
{
    public interface IDriverAuthService
    {
        Task<(bool Success, string? Error, Driver?)> LoginAsync(string username, string password);
    }
}
