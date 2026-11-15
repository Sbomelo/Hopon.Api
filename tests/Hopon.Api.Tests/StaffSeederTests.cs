using Hopon.Api.Data;
using Hopon.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hopon.Api.Tests;

public class StaffSeederTests
{
    [Theory]
    [InlineData("Production", false, 0)]
    [InlineData("Production", true, 1)]
    [InlineData("Development", false, 1)]
    [InlineData("Development", true, 1)]
    public async Task Bootstrap_respects_configuration_environment_and_existing_admins(string environment, bool configured, int expected)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(configured
            ? new Dictionary<string, string?> { ["Admin:Username"] = "configured-admin", ["Admin:Password"] = "Test-bootstrap-password" }
            : new Dictionary<string, string?>()).Build();
        var logger = new CapturingLogger();
        var seeder = new StaffSeeder(db, config, new TestEnvironment { EnvironmentName = environment }, logger);
        await seeder.SeedAsync();
        Assert.Equal(expected, await db.AdminUsers.CountAsync());
        if (expected == 0)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Configure Admin:Username"));
            return;
        }
        var admin = await db.AdminUsers.SingleAsync();
        Assert.Equal(configured ? "configured-admin" : "admin", admin.Username);
        if (configured)
        {
            Assert.True(BCrypt.Net.BCrypt.Verify("Test-bootstrap-password", admin.PasswordHash));
            Assert.Empty(logger.Messages);
        }
        else
        {
            var message = Assert.Single(logger.Messages);
            var password = message.Split("Password: ")[1];
            Assert.True(password.Length >= 24);
            Assert.True(BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash));
        }
        admin.IsActive = false;
        await db.SaveChangesAsync();
        var previousLogCount = logger.Messages.Count;
        await seeder.SeedAsync();
        Assert.Equal(1, await db.AdminUsers.CountAsync());
        Assert.Equal(previousLogCount, logger.Messages.Count);
        Assert.False(admin.IsActive);
    }

    private sealed class CapturingLogger : ILogger<StaffSeeder>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Hopon.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
