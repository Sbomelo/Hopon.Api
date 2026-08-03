using Hopon.Api.Data;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class DriverAuthService : IDriverAuthService
    {
        private readonly HoponDbContext _db;

        public DriverAuthService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<(bool Success, string? Error, Models.Driver?)> LoginAsync(string username, string password)
        {
            var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Username == username);

            if (driver is null || !driver.IsActive)
                return (false, "Invalid userame or password.", null);

            var isMatch = BCrypt.Net.BCrypt.Verify(password, driver.PasswordHash);

            if (!isMatch)
                return (false, "Invalid username or password", null);

            return (true, null, driver);
        }
    }
}
