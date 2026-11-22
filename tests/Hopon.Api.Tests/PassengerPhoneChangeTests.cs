using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.DTOs.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class PassengerPhoneChangeTests
{
    [Fact]
    public async Task Phone_change_code_is_scoped_to_the_passenger_and_preserves_the_account()
    {
        await using var app = new HoponWebApplicationFactory();
        var (passenger, token) = await app.CreatePassengerAsync("+27600100000");
        var (_, otherToken) = await app.CreatePassengerAsync("+27600100001");
        using var client = app.NewClient();
        var newPhone = "+27600100002";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/profile")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var profile = await client.GetFromJsonAsync<PassengerProfileDto>("/api/auth/profile");
        Assert.Equal(passenger.FullName, profile!.FullName);
        Assert.Equal(passenger.PhoneNumber, profile.PhoneNumber);

        var taken = await client.PostAsJsonAsync("/api/auth/phone-change/request", new { PhoneNumber = "+27600100001" });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Empty(app.Sms.Messages);
        var requested = await client.PostAsJsonAsync("/api/auth/phone-change/request", new { PhoneNumber = "060 010 0002" });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var message = Assert.Single(app.Sms.Messages);
        Assert.Equal(newPhone, message.To);
        var code = Regex.Match(message.Message, @"\b\d{6}\b").Value;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var otherAttempt = await client.PostAsJsonAsync("/api/auth/phone-change/verify", new { PhoneNumber = newPhone, Code = code });
        Assert.Equal(HttpStatusCode.BadRequest, otherAttempt.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var loginAttempt = await client.PostAsJsonAsync("/api/auth/otp/verify", new { PhoneNumber = newPhone, Code = code });
        Assert.Equal(HttpStatusCode.Unauthorized, loginAttempt.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var incorrect = await client.PostAsJsonAsync("/api/auth/phone-change/verify", new
        {
            PhoneNumber = newPhone,
            Code = code == "000000" ? "000001" : "000000"
        });
        Assert.Equal(HttpStatusCode.BadRequest, incorrect.StatusCode);
        var verified = await client.PostAsJsonAsync("/api/auth/phone-change/verify", new { PhoneNumber = newPhone, Code = code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var session = await verified.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.Equal(passenger.Id, session!.UserId);
        Assert.Equal(newPhone, session.PhoneNumber);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/profile")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        profile = await client.GetFromJsonAsync<PassengerProfileDto>("/api/auth/profile");
        Assert.Equal(newPhone, profile!.PhoneNumber);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var stored = await db.OtpRequests.SingleAsync();
        Assert.Equal(passenger.Id, stored.PhoneChangeUserId);
        Assert.True(stored.IsUsed);
        Assert.NotEqual(code, stored.OtpCodeHash);
        Assert.Equal(newPhone, (await db.Users.SingleAsync(user => user.Id == passenger.Id)).PhoneNumber);
    }
}
