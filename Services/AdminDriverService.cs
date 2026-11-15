using Hopon.Api.Data;
using Hopon.Api.DTOs.Admin;
using Hopon.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class AdminDriverService(HoponDbContext db)
{
    public Task<List<Driver>> ListAsync() => db.Drivers.AsNoTracking().OrderBy(d => d.FullName).ToListAsync();
    public Task<Driver?> FindAsync(int id) => db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);

    public async Task<bool> CreateAsync(CreateDriverInput input)
    {
        var username = input.Username.Trim();
        if (await db.Drivers.AnyAsync(d => d.Username == username)) return false;
        var driver = new Driver
        {
            FullName = input.FullName.Trim(),
            PhoneNumber = input.PhoneNumber.Trim(),
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.Password)
        };
        db.Drivers.Add(driver);
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The unique index also covers two admins creating the same username concurrently.
            db.Entry(driver).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var driver = await db.Drivers.FindAsync(id);
        if (driver is null) return false;
        driver.IsActive = isActive;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResetPasswordAsync(int id, string password)
    {
        var driver = await db.Drivers.FindAsync(id);
        if (driver is null) return false;
        driver.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        await db.SaveChangesAsync();
        return true;
    }
}
