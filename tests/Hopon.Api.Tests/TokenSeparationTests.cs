using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Hopon.Api.Tests;

public class TokenSeparationTests
{
    private static readonly DateTime FutureExpiry = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PastExpiry = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overlapping_ids_do_not_allow_driver_and_passenger_impersonation()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var passenger = new User { Id = 101, FullName = "Test passenger", PhoneNumber = "+27780000101" };
        db.Users.Add(passenger);
        db.EmergencyContacts.Add(new EmergencyContact { UserId = 101, Name = "Test contact", PhoneNumber = "+27780000102" });
        await db.SaveChangesAsync();

        var login = await client.PostAsJsonAsync("/api/driver/auth/login", new { Username = "driver-one", Password = StaffApplication.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var result = await login.Content.ReadFromJsonAsync<JsonElement>();
        var driverToken = result.GetProperty("token").GetString()!;
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(driverToken).Claims.ToList();
        Assert.Contains(claims, c => c.Type == "username" && c.Value == "driver-one");
        Assert.DoesNotContain(claims, c => c.Type == "userName");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
        foreach (var path in new[] { "/api/trips/my-trips", "/api/notifications/my", "/api/emergency-contact", "/hubs/trip" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/trip/negotiate?negotiateVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/emergency-contact")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/emergency-contact", new { Name = "Changed", PhoneNumber = "+27780000103" })).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken(passenger).Token);
        foreach (var path in new[] { "/api/trips/my-trips", "/api/notifications/my", "/api/emergency-contact" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/driver/my-trip")).StatusCode);
    }

    [Fact]
    public async Task OTP_login_can_load_map_and_join_only_a_ticketed_trip()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var (passenger, _) = await app.CreatePassengerAsync("+27600000101");
        var (otherPassenger, otherToken) = await app.CreatePassengerAsync("+27600000102");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.Tickets.AddRange(
            new Ticket { UserId = passenger.Id, TripId = 101, TicketReference = "TEST-PASSENGER-A" },
            new Ticket { UserId = otherPassenger.Id, TripId = 102, TicketReference = "TEST-PASSENGER-B" });
        await db.SaveChangesAsync();

        var requested = await client.PostAsJsonAsync("/api/auth/otp/request", new { passenger.PhoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var sms = Assert.Single(app.Sms.Messages);
        Assert.Equal(passenger.PhoneNumber, sms.To);
        var code = sms.Message.Split(' ')[4].TrimEnd('.');
        var verified = await client.PostAsJsonAsync("/api/auth/otp/verify", new { passenger.PhoneNumber, Code = code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var result = await verified.Content.ReadFromJsonAsync<JsonElement>();
        var token = result.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
        using var otherClient = app.NewClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        foreach (var endpoint in new[] { "access-check", "map-data", "history" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/trips/101/{endpoint}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync($"/api/trips/102/{endpoint}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trips/102/{endpoint}")).StatusCode);
        }

        await using var hub = new HubConnectionBuilder().WithUrl("http://localhost/hubs/trip", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.WebSocketFactory = async (context, ct) =>
            {
                var uri = new UriBuilder(context.Uri) { Query = "access_token=" + Uri.EscapeDataString(token) };
                return await app.Server.CreateWebSocketClient().ConnectAsync(uri.Uri, ct);
            };
        }).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await hub.StartAsync(timeout.Token);
        await hub.InvokeAsync("JoinTrip", 101, timeout.Token);
        var denied = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinTrip", 102, timeout.Token));
        Assert.Contains("You do not have access", denied.Message);
    }

    [Theory]
    [InlineData("/api/trips/my-trips")]
    [InlineData("/api/notifications/my")]
    [InlineData("/api/emergency-contact")]
    [InlineData("/api/driver/my-trip")]
    [InlineData("/hubs/trip")]
    public async Task Old_roleless_tokens_are_forbidden(string path)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(app, role: null));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Query_tokens_are_limited_to_hubs_and_driver_query_tokens_are_forbidden()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var token = CreateToken(app, "Passenger");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trips/my-trips?access_token=" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/trip/negotiate?negotiateVersion=1&access_token=" + token, null)).StatusCode);
        var (_, driverToken) = await app.CreateDriverAsync("driver-query-test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/hubs/trip?access_token=" + driverToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/trip/negotiate?negotiateVersion=1&access_token=" + driverToken, null)).StatusCode);
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.OK)]
    [InlineData(true, false, HttpStatusCode.Unauthorized)]
    [InlineData(false, true, HttpStatusCode.Unauthorized)]
    public async Task Signature_and_expiry_are_enforced(bool expired, bool wrongKey, HttpStatusCode expected)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(TimeSpan.FromMinutes(1), options.TokenValidationParameters.ClockSkew);
        var expiry = expired ? PastExpiry : FutureExpiry;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(app, "Passenger", expiry, wrongKey));
        Assert.Equal(expected, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
    }

    private static string CreateToken(StaffApplication app, string? role, DateTime? expiry = null, bool wrongKey = false)
    {
        var config = app.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        var key = wrongKey ? "Different-test-only-key-at-least-32-characters" : config["Key"]!;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: config["Issuer"], audience: config["Audience"], claims: claims,
            expires: expiry ?? FutureExpiry,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    }
}
