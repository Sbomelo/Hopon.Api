using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Hopon.Api.Tests;

public class StaffAuthenticationTests
{
    [Theory]
    [InlineData("/Admin/Trips")]
    [InlineData("/Admin/Drivers")]
    [InlineData("/Driver/Trip")]
    public async Task Anonymous_staff_pages_redirect_to_login(string path)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Staff/Login", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
    }

    [Theory]
    [InlineData("Admin", "admin-test", "/Admin/Trips", "/Driver/Trip")]
    [InlineData("Driver", "driver-one", "/Driver/Trip", "/Admin/Trips")]
    public async Task Cookies_enforce_roles_and_do_not_authenticate_APIs(string role, string username, string own, string other)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var login = await Login(client, role, username);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(own, login.Headers.Location!.OriginalString);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(v => v.Contains("StaffCookie"));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(own)).StatusCode);
        var denied = await client.GetAsync(other);
        Assert.Contains("/Staff/Denied", denied.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(denied.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
    }

    [Fact]
    public async Task Drivers_only_see_and_act_on_their_own_trip_and_stops()
    {
        using var app = new StaffApplication();
        using var first = app.NewClient();
        using var second = app.NewClient();
        await Login(first, "Driver", "driver-one");
        await Login(second, "Driver", "driver-two");
        var firstPage = await first.GetStringAsync("/Driver/Trip");
        var secondPage = await second.GetStringAsync("/Driver/Trip");
        Assert.Contains("First driver route", firstPage);
        Assert.DoesNotContain("First driver route", secondPage);
        Assert.Contains("Second driver route", secondPage);
        Assert.Equal(HttpStatusCode.OK, (await Post(first, "/Driver/Trip?handler=StartTrip", "/Driver/Trip")).StatusCode);
        // Supplying another driver's trip ID cannot override the claim-derived selection.
        await Post(second, "/Driver/Trip?handler=StartTrip", "/Driver/Trip", ("tripId", "101"));
        foreach (var handler in new[] { "MarkArrived", "MarkDeparted" })
        {
            var result = await Post(second, "/Driver/Trip?handler=" + handler, "/Driver/Trip", ("TripStopId", "101"));
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            Assert.Equal(TripStatus.InProgress, (await db.Trips.FindAsync(101))!.Status);
            Assert.Equal(TripStatus.InProgress, (await db.Trips.FindAsync(102))!.Status);
            var stop = (await db.TripStops.FindAsync(101))!;
            Assert.Equal(TripStopStatus.Pending, stop.Status);
            Assert.Null(stop.ActualArrival);
            Assert.Null(stop.ActualDeparture);
            var service = scope.ServiceProvider.GetRequiredService<IDriverTripService>();
            var otherTrip = (await db.Trips.FindAsync(102))!;
            Assert.False((await service.MarkStopArrivedAsync(otherTrip, 101)).Success);
            Assert.False((await service.MarkStopDepartedAsync(otherTrip, 101)).Success);
        }
        Assert.Equal(HttpStatusCode.OK, (await Post(first, "/Driver/Trip?handler=MarkArrived", "/Driver/Trip", ("TripStopId", "101"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(first, "/Driver/Trip?handler=MarkDeparted", "/Driver/Trip", ("TripStopId", "101"))).StatusCode);
        using var finalScope = app.Services.CreateScope();
        Assert.Equal(TripStopStatus.Departed, (await finalScope.ServiceProvider.GetRequiredService<HoponDbContext>().TripStops.FindAsync(101))!.Status);
    }

    [Fact]
    public async Task Login_errors_are_generic_and_logout_requires_antiforgery_POST()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        foreach (var role in new[] { "Admin", "Driver", "Unknown" })
        {
            var failure = await Login(client, role, "missing");
            Assert.Contains("Invalid username or password", await failure.Content.ReadAsStringAsync());
            Assert.DoesNotContain(failure.Headers.TryGetValues("Set-Cookie", out var values) ? values : [], c => c.Contains("StaffCookie"));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Staff/Login", new FormUrlEncodedContent([]))).StatusCode);
        await Login(client, "Driver", "driver-one");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/Staff/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Staff/Logout", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Driver/Trip")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Staff/Logout", "/Driver/Trip")).StatusCode);
        Assert.Contains("/Staff/Login", (await client.GetAsync("/Driver/Trip")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Admin_can_create_reset_deactivate_and_reactivate_drivers()
    {
        using var app = new StaffApplication();
        using var admin = app.NewClient();
        using var driver = app.NewClient();
        await Login(admin, "Admin", "admin-test");
        var fields = new[] { ("Input.FullName", "New driver"), ("Input.PhoneNumber", "+27000000001"), ("Input.Username", "new-driver"), ("Input.Password", "New-password-123") };
        var create = await Post(admin, "/Admin/Drivers/Create", "/Admin/Drivers/Create", fields);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var duplicate = await Post(admin, "/Admin/Drivers/Create", "/Admin/Drivers/Create", fields);
        Assert.Contains("username is already in use", await duplicate.Content.ReadAsStringAsync());
        fields[2] = ("Input.Username", "short-password");
        fields[3] = ("Input.Password", "short");
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, "/Admin/Drivers/Create", "/Admin/Drivers/Create", fields)).StatusCode);
        int id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var created = await db.Drivers.SingleAsync(d => d.Username == "new-driver");
            id = created.Id;
            Assert.True(BCrypt.Net.BCrypt.Verify("New-password-123", created.PasswordHash));
            Assert.False(await db.Drivers.AnyAsync(d => d.Username == "short-password"));
        }
        await Login(driver, "Driver", "new-driver", "New-password-123");
        await Post(admin, "/Admin/Drivers?handler=SetActive", "/Admin/Drivers", ("id", id.ToString()), ("isActive", "false"));
        Assert.Contains("/Staff/Login", (await driver.GetAsync("/Driver/Trip")).Headers.Location!.OriginalString);
        Assert.Contains("Invalid username or password", await (await Login(driver, "Driver", "new-driver", "New-password-123")).Content.ReadAsStringAsync());
        await Post(admin, "/Admin/Drivers?handler=SetActive", "/Admin/Drivers", ("id", id.ToString()), ("isActive", "true"));
        var resetPath = "/Admin/Drivers/ResetPassword/" + id;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, resetPath, resetPath, ("Password", "Reset-password-123"))).StatusCode);
        Assert.Contains("Invalid username or password", await (await Login(driver, "Driver", "new-driver", "New-password-123")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Login(driver, "Driver", "new-driver", "Reset-password-123")).StatusCode);
    }

    [Fact]
    public async Task Existing_driver_and_passenger_JWTs_still_work_and_do_not_open_Razor_pages()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var login = await client.PostAsJsonAsync("/api/driver/auth/login", new { Username = "driver-one", Password = StaffApplication.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
        Assert.Contains("/Staff/Login", (await client.GetAsync("/Driver/Trip")).Headers.Location!.OriginalString);
        using var scope = app.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(1))!).Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string role, string username, string password = StaffApplication.Password) =>
        Post(client, "/Staff/Login", "/Staff/Login", ("Input.Role", role), ("Input.Username", username), ("Input.Password", password));

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, string formPage, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected an antiforgery token in " + formPage);
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }
}

internal sealed class StaffApplication : WebApplicationFactory<Program>
{
    public const string Password = "Test-only-password-123";
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public StaffApplication()
    {
        connection.Open();
        using var db = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var hash = BCrypt.Net.BCrypt.HashPassword(Password);
        db.Drivers.AddRange(
            new Driver { Id = 101, FullName = "Driver One", Username = "driver-one", PasswordHash = hash },
            new Driver { Id = 102, FullName = "Driver Two", Username = "driver-two", PasswordHash = hash });
        for (var id = 101; id <= 102; id++)
        {
            var trip = new Trip
            {
                Id = id, DriverId = id, Bus = new Bus { RegistrationNumber = "TEST-" + id },
                BusRoute = new BusRoute { Name = id == 101 ? "First driver route" : "Second driver route" },
                ScheduledDeparture = DateTime.UtcNow, ScheduledArrival = DateTime.UtcNow.AddHours(4)
            };
            trip.TripStops.Add(new TripStop { Id = id, Stop = new Stop { Name = "Test stop " + id }, SequenceOrder = 1 });
            db.Trips.Add(trip);
        }
        db.SaveChanges();
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin-test", ["Admin:Password"] = Password,
            ["Jwt:Key"] = "Test-only-signing-key-at-least-32-characters", ["Jwt:Issuer"] = "Hopon-tests",
            ["Jwt:Audience"] = "Hopon-tests", ["Jwt:ExpiryHours"] = "1"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<Hopon.Api.Services.Sms.ISmsSender>();
            services.AddSingleton<Hopon.Api.Services.Sms.ISmsSender>(new FakeSmsSender());
            services.RemoveAll<HoponDbContext>();
            services.RemoveAll<DbContextOptions<HoponDbContext>>();
            services.AddDbContext<HoponDbContext>(options => options.UseSqlite(connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}
