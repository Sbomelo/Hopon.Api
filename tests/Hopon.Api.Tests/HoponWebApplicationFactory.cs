using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Hopon.Api.Services.Notifications;
using Hopon.Api.Services.Sms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Hopon.Api.Tests;

internal class HoponWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    internal static readonly DateTimeOffset TestNow = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    public const string Password = "Test-only-password-123";
    protected static readonly string TestPasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4);

    public FakeTimeProvider Clock { get; } = new(TestNow);
    public FakeSmsSender Sms { get; } = new();

    public HoponWebApplicationFactory()
    {
        connection.Open();
        // Program runs the staff bootstrap before the server accepts requests.
        using var db = OpenDbContext();
        db.Database.EnsureCreated();
    }

    protected HoponDbContext OpenDbContext() => new(new DbContextOptionsBuilder<HoponDbContext>()
        .UseSqlite(connection).Options);

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<(User User, string Token)> CreatePassengerAsync(string phoneNumber)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var user = new User { FullName = "Test passenger", PhoneNumber = phoneNumber };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(user).Token;
        return (user, token);
    }

    public async Task<(Driver Driver, string Token)> CreateDriverAsync(string username)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var driver = new Driver
        {
            FullName = "Test driver", Username = username, PasswordHash = TestPasswordHash
        };
        db.Drivers.Add(driver);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(driver).Token;
        return (driver, token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin-test",
            ["Admin:Password"] = Password,
            ["Demo:SeedOnStartup"] = "false",
            ["Sms:Provider"] = "Logging",
            ["Notifications:SmsEnabled"] = "false",
            ["Jwt:Key"] = "Test-only-signing-key-at-least-32-characters",
            ["Jwt:Issuer"] = "Hopon-tests",
            ["Jwt:Audience"] = "Hopon-tests",
            ["Jwt:ExpiryHours"] = "1"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<HoponDbContext>();
            services.RemoveAll<DbContextOptions<HoponDbContext>>();
            services.AddDbContext<HoponDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            var workers = services.Where(service => service.ServiceType == typeof(IHostedService) &&
                service.ImplementationType is not null &&
                typeof(BackgroundService).IsAssignableFrom(service.ImplementationType)).ToArray();
            foreach (var worker in workers)
                services.Remove(worker);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
    }
}
