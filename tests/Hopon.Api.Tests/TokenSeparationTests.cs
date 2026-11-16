using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Hopon.Api.Tests;

public class TokenSeparationTests
{
    [Fact]
    public async Task Overlapping_ids_do_not_allow_driver_and_passenger_impersonation()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var passenger = new User { Id = 101, FullName = "Test passenger", PhoneNumber = "+27000000101" };
        db.Users.Add(passenger);
        db.EmergencyContacts.Add(new EmergencyContact { UserId = 101, Name = "Test contact", PhoneNumber = "+27000000102" });
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
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/emergency-contact", new { Name = "Changed", PhoneNumber = "+27000000103" })).StatusCode);

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
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var passenger = new User { Id = 101, FullName = "Test passenger", PhoneNumber = "+27000000101" };
        db.Users.Add(passenger);
        db.Tickets.Add(new Ticket { UserId = 101, TripId = 101, TicketReference = "TEST-PASSENGER" });
        await db.SaveChangesAsync();

        var requested = await client.PostAsJsonAsync("/api/auth/otp/request", new { passenger.PhoneNumber });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var otp = await requested.Content.ReadFromJsonAsync<JsonElement>();
        var verified = await client.PostAsJsonAsync("/api/auth/otp/verify", new { passenger.PhoneNumber, Code = otp.GetProperty("devOnlyCode").GetString() });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var result = await verified.Content.ReadFromJsonAsync<JsonElement>();
        var token = result.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trips/101/map-data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trips/102/map-data")).StatusCode);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await app.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/hubs/trip?access_token=" + token), timeout.Token);
        await Send(socket, "{\"protocol\":\"json\",\"version\":1}", timeout.Token);
        Assert.Equal("{}", (await Receive(socket, timeout.Token)).GetRawText());
        await Send(socket, "{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinTrip\",\"arguments\":[101]}", timeout.Token);
        var joined = await Receive(socket, timeout.Token);
        Assert.Equal(3, joined.GetProperty("type").GetInt32());
        Assert.False(joined.TryGetProperty("error", out _));
        await Send(socket, "{\"type\":1,\"invocationId\":\"2\",\"target\":\"JoinTrip\",\"arguments\":[102]}", timeout.Token);
        Assert.Contains("You do not have access", (await Receive(socket, timeout.Token)).GetProperty("error").GetString());
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
        var driverToken = CreateToken(app, "Driver");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/hubs/trip?access_token=" + driverToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/trip/negotiate?negotiateVersion=1&access_token=" + driverToken, null)).StatusCode);
    }

    [Theory]
    [InlineData(-30, false, HttpStatusCode.OK)]
    [InlineData(-120, false, HttpStatusCode.Unauthorized)]
    [InlineData(3600, true, HttpStatusCode.Unauthorized)]
    public async Task Signature_and_one_minute_expiry_tolerance_are_enforced(int expirySeconds, bool wrongKey, HttpStatusCode expected)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(app, "Passenger", expirySeconds, wrongKey));
        Assert.Equal(expected, (await client.GetAsync("/api/trips/my-trips")).StatusCode);
    }

    private static string CreateToken(StaffApplication app, string? role, int expirySeconds = 3600, bool wrongKey = false)
    {
        var config = app.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        var key = wrongKey ? "Different-test-only-key-at-least-32-characters" : config["Key"]!;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: config["Issuer"], audience: config["Audience"], claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expirySeconds),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    }

    private static Task Send(WebSocket socket, string json, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json + "\u001e")), WebSocketMessageType.Text, true, token);

    private static async Task<JsonElement> Receive(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count).TrimEnd('\u001e'));
        return json.RootElement.Clone();
    }
}
