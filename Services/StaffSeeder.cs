using System.Security.Cryptography;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class StaffSeeder(HoponDbContext db, IConfiguration configuration,
    IHostEnvironment environment, ILogger<StaffSeeder> logger)
{
    public async Task SeedAsync()
    {
        if (await db.AdminUsers.AnyAsync()) return;

        var username = configuration["Admin:Username"];
        var password = configuration["Admin:Password"];
        var generatedPassword = false;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            if (!environment.IsDevelopment())
            {
                logger.LogWarning("No admin exists. Configure Admin:Username and Admin:Password to create the first admin.");
                return;
            }

            username = "admin";
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            generatedPassword = true;
        }

        db.AdminUsers.Add(new AdminUser
        {
            Username = username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        });
        await db.SaveChangesAsync();

        // Only the generated Development credential is logged, once after creation.
        if (generatedPassword)
            logger.LogWarning("Development admin created. Username: admin Password: {Password}", password);
    }
}
