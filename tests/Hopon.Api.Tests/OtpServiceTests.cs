using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hopon.Api.Tests;

public class OtpServiceTests
{
    private const string Phone = "+27781234567";

    [Fact]
    public async Task New_request_invalidates_all_older_unused_codes_for_only_that_phone()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = await CreateServiceAsync(db);
        var oldHash = BCrypt.Net.BCrypt.HashPassword("123456");
        db.OtpRequests.AddRange(
            OldRequest(Phone, oldHash), OldRequest(Phone, oldHash),
            OldRequest("+27821234567", oldHash));
        await db.SaveChangesAsync();

        var result = await service.RequestOtpAsync(Phone);

        Assert.True(result.Success);
        Assert.Matches("^[0-9]{6}$", result.DevOnlyCode!);
        db.ChangeTracker.Clear();
        var requests = await db.OtpRequests.Where(o => o.PhoneNumber == Phone).OrderBy(o => o.Id).ToListAsync();
        Assert.Equal(3, requests.Count);
        Assert.All(requests.Take(2), o => Assert.True(o.IsUsed));
        Assert.False(requests[2].IsUsed);
        Assert.NotEqual(result.DevOnlyCode, requests[2].OtpCodeHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(result.DevOnlyCode, requests[2].OtpCodeHash));
        Assert.Equal(TimeSpan.FromMinutes(10), requests[2].ExpiresAt - requests[2].CreatedAt);
        Assert.False((await db.OtpRequests.SingleAsync(o => o.PhoneNumber != Phone)).IsUsed);

        Assert.True((await service.VerifyOtpAsync(Phone, result.DevOnlyCode!)).Success);
        Assert.False((await service.VerifyOtpAsync(Phone, "123456")).Success);
        Assert.False((await service.VerifyOtpAsync(Phone, result.DevOnlyCode!)).Success);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = await CreateServiceAsync(db);
        var request = await service.RequestOtpAsync(Phone);
        var otp = await db.OtpRequests.SingleAsync();
        otp.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var result = await service.VerifyOtpAsync(Phone, request.DevOnlyCode!);

        Assert.False(result.Success);
        Assert.Contains("expired", result.Error);
        Assert.False(otp.IsUsed);
    }

    [Fact]
    public async Task Five_wrong_attempts_block_even_the_right_code()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = await CreateServiceAsync(db);
        var request = await service.RequestOtpAsync(Phone);
        var wrongCode = request.DevOnlyCode == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await service.VerifyOtpAsync(Phone, wrongCode);
            Assert.False(wrong.Success);
            Assert.Equal("Incorrect code.", wrong.Error);
        }

        var result = await service.VerifyOtpAsync(Phone, request.DevOnlyCode!);

        Assert.False(result.Success);
        Assert.Contains("Too many incorrect attempts", result.Error);
        db.ChangeTracker.Clear();
        var otp = await db.OtpRequests.SingleAsync();
        Assert.Equal(5, otp.AttemptCount);
        Assert.False(otp.IsUsed);
    }

    [Fact]
    public async Task Three_requests_per_ten_minutes_are_allowed_then_the_window_resets()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = await CreateServiceAsync(db);
        for (var request = 0; request < 3; request++)
            Assert.True((await service.RequestOtpAsync(Phone)).Success);

        var blocked = await service.RequestOtpAsync(Phone);
        Assert.False(blocked.Success);
        Assert.Contains("Too many OTP requests", blocked.Error);
        Assert.Null(blocked.DevOnlyCode);
        Assert.Equal(3, await db.OtpRequests.CountAsync());
        Assert.Equal(1, await db.OtpRequests.CountAsync(o => !o.IsUsed));

        var oldest = await db.OtpRequests.OrderBy(o => o.CreatedAt).FirstAsync();
        oldest.CreatedAt = DateTime.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();
        Assert.True((await service.RequestOtpAsync(Phone)).Success);
        Assert.Equal(4, await db.OtpRequests.CountAsync());
    }

    [Fact]
    public async Task Inactive_and_unknown_users_receive_the_same_generic_errors()
    {
        await using var database = await TestDb.CreateAsync();
        var db = database.Context;
        var service = await CreateServiceAsync(db);
        var request = await service.RequestOtpAsync(Phone);
        (await db.Users.SingleAsync(u => u.PhoneNumber == Phone)).IsActive = false;
        await db.SaveChangesAsync();

        var inactiveRequest = await service.RequestOtpAsync(Phone);
        var unknownRequest = await service.RequestOtpAsync("+27821234567");
        Assert.False(inactiveRequest.Success);
        Assert.Equal(unknownRequest, inactiveRequest);
        Assert.Equal("Unable to process this request.", inactiveRequest.Error);

        var inactiveVerify = await service.VerifyOtpAsync(Phone, request.DevOnlyCode!);
        var unknownVerify = await service.VerifyOtpAsync("+27821234567", request.DevOnlyCode!);
        Assert.False(inactiveVerify.Success);
        Assert.Equal(unknownVerify, inactiveVerify);
        Assert.Equal(1, await db.OtpRequests.CountAsync());
        Assert.False((await db.OtpRequests.SingleAsync()).IsUsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Response_exposure_is_opt_in_and_code_is_sent_by_sms(bool expose)
    {
        await using var database = await TestDb.CreateAsync();
        database.Context.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await database.Context.SaveChangesAsync();
        var sender = new FakeSmsSender();
        var service = new OtpService(database.Context, sender, Options.Create(new OtpOptions { ExposeCodeInResponse = expose }));
        var request = await service.RequestOtpAsync(Phone);
        Assert.True(request.Success);
        Assert.Equal(expose, request.DevOnlyCode is not null);
        var sms = Assert.Single(sender.Messages);
        Assert.Equal(Phone, sms.To);
        Assert.Matches(@"^Your Hopon code is [0-9]{6}\. It expires in 10 minutes\.$", sms.Message);
        var code = sms.Message.Split(' ')[4].TrimEnd('.');
        Assert.True(BCrypt.Net.BCrypt.Verify(code, (await database.Context.OtpRequests.SingleAsync()).OtpCodeHash));
        if (expose) Assert.Equal(code, request.DevOnlyCode);
    }

    [Fact]
    public async Task Failed_delivery_deletes_request_preserves_previous_code_and_does_not_consume_rate_limit()
    {
        await using var database = await TestDb.CreateAsync();
        database.Context.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await database.Context.SaveChangesAsync();
        var sender = new FakeSmsSender();
        var service = new OtpService(database.Context, sender, Options.Create(new OtpOptions { ExposeCodeInResponse = true }));
        var previous = await service.RequestOtpAsync(Phone);
        sender.Result = new(false, "test failure", null);
        for (var i = 0; i < 4; i++)
        {
            var failed = await service.RequestOtpAsync(Phone);
            Assert.False(failed.Success);
            Assert.Equal("We couldn't send the SMS. Please try again.", failed.Error);
            Assert.Null(failed.DevOnlyCode);
            Assert.False((await database.Context.OtpRequests.SingleAsync()).IsUsed);
        }
        Assert.True((await service.VerifyOtpAsync(Phone, previous.DevOnlyCode!)).Success);
        sender.Result = new(true, null, "test-message");
        Assert.True((await service.RequestOtpAsync(Phone)).Success);
        Assert.Equal(2, await database.Context.OtpRequests.CountAsync());
    }

    [Fact]
    public void Code_exposure_defaults_to_false() => Assert.False(new OtpOptions().ExposeCodeInResponse);

    private static OtpRequest OldRequest(string phone, string hash) => new()
    {
        PhoneNumber = phone, OtpCodeHash = hash,
        CreatedAt = DateTime.UtcNow.AddMinutes(-2), ExpiresAt = DateTime.UtcNow.AddMinutes(8)
    };

    private static async Task<OtpService> CreateServiceAsync(HoponDbContext db)
    {
        db.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await db.SaveChangesAsync();
        return new OtpService(db, new FakeSmsSender(), Options.Create(new OtpOptions { ExposeCodeInResponse = true }));
    }
}
