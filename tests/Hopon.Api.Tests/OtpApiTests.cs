using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class OtpApiTests
{
    private const string Phone = "+27781234567";

    [Theory]
    [InlineData("0781234567", "+27 78 123 4567")]
    [InlineData("078 123 4567", "27781234567")]
    [InlineData("+27 78 123 4567", "0027781234567")]
    [InlineData("27781234567", "(078) 123-4567")]
    [InlineData("0027781234567", "0781234567")]
    [InlineData("(078) 123-4567", "078 123 4567")]
    public async Task Request_and_verify_accept_all_common_formats(string requestPhone, string verifyPhone)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await db.SaveChangesAsync();

        var request = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = requestPhone });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var otp = await request.Content.ReadFromJsonAsync<JsonElement>();
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify",
            new { PhoneNumber = verifyPhone, Code = otp.GetProperty("devOnlyCode").GetString() });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var login = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Phone, login.GetProperty("phoneNumber").GetString());
        Assert.False(string.IsNullOrEmpty(login.GetProperty("token").GetString()));
        var stored = await db.OtpRequests.SingleAsync();
        Assert.Equal(Phone, stored.PhoneNumber);
        Assert.True(stored.IsUsed);
    }

    [Fact]
    public async Task Different_formats_share_the_request_rate_limit()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await db.SaveChangesAsync();
        foreach (var phone in new[] { "0781234567", "+27 78 123 4567", "0027781234567" })
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = phone })).StatusCode);

        var blocked = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = "(078) 123-4567" });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("Too many OTP requests", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(3, await db.OtpRequests.CountAsync());
    }

    [Theory]
    [InlineData("/api/auth/otp/request")]
    [InlineData("/api/auth/otp/verify")]
    public async Task Invalid_phone_returns_bad_request_before_creating_an_otp(string path)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var response = await client.PostAsJsonAsync(path, new { PhoneNumber = "0111234567", Code = "123456" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<HoponDbContext>().OtpRequests.ToListAsync());
    }

    [Fact]
    public async Task Deactivation_between_request_and_verify_prevents_login()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var user = new User { PhoneNumber = Phone, FullName = "OTP test passenger" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var request = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = Phone });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var otp = await request.Content.ReadFromJsonAsync<JsonElement>();
        user.IsActive = false;
        await db.SaveChangesAsync();

        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify",
            new { PhoneNumber = Phone, Code = otp.GetProperty("devOnlyCode").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
        Assert.Contains("Unable to process this request.", await verify.Content.ReadAsStringAsync());
        var inactive = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = Phone });
        var unknown = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = "+27821234567" });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal(inactive.StatusCode, unknown.StatusCode);
        Assert.Equal(await inactive.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Configuration_can_disable_response_code_even_in_development()
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Otp:ExposeCodeInResponse"] = "false" })));
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await db.SaveChangesAsync();

        var request = await client.PostAsJsonAsync("/api/auth/otp/request", new { PhoneNumber = Phone });

        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var otp = await request.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, otp.GetProperty("devOnlyCode").ValueKind);
    }

    [Fact]
    public async Task Emergency_contact_create_and_update_store_normalized_numbers_and_invalid_input_returns_validation_400()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var user = new User { PhoneNumber = Phone, FullName = "OTP test passenger" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(user).Token);

        foreach (var phone in new[] { "(078) 123-4567", "0027781234567" })
        {
            var saved = await client.PutAsJsonAsync("/api/emergency-contact", new { Name = "Test contact", PhoneNumber = phone });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var contact = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Phone, contact.GetProperty("phoneNumber").GetString());
            Assert.Equal(Phone, (await db.EmergencyContacts.AsNoTracking().SingleAsync()).PhoneNumber);
        }

        var invalid = await client.PutAsJsonAsync("/api/emergency-contact", new { Name = "Invalid", PhoneNumber = "0111234567" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("PhoneNumber", out _));
        var unchanged = await db.EmergencyContacts.AsNoTracking().SingleAsync();
        Assert.Equal(Phone, unchanged.PhoneNumber);
        Assert.Equal("Test contact", unchanged.Name);
    }
}
