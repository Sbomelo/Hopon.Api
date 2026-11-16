using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    [InlineData("Development", true)]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", true)]
    [InlineData("Staging", false)]
    public async Task Response_exposure_is_opt_in_and_logging_is_development_only(string environment, bool expose)
    {
        await using var database = await TestDb.CreateAsync();
        var logger = new CapturingLogger();
        var service = await CreateServiceAsync(database.Context, environment, expose, logger);

        var request = await service.RequestOtpAsync(Phone);

        Assert.True(request.Success);
        Assert.Equal(expose, request.DevOnlyCode is not null);
        if (environment == "Development")
        {
            var message = Assert.Single(logger.Messages);
            var loggedCode = message.Split(" : ")[1];
            Assert.Matches("^[0-9]{6}$", loggedCode);
            Assert.True(BCrypt.Net.BCrypt.Verify(loggedCode, (await database.Context.OtpRequests.SingleAsync()).OtpCodeHash));
            if (expose) Assert.Equal(loggedCode, request.DevOnlyCode);
        }
        else
        {
            Assert.Empty(logger.Messages);
        }
    }

    [Fact]
    public void Code_exposure_defaults_to_false() => Assert.False(new OtpOptions().ExposeCodeInResponse);

    private static OtpRequest OldRequest(string phone, string hash) => new()
    {
        PhoneNumber = phone, OtpCodeHash = hash,
        CreatedAt = DateTime.UtcNow.AddMinutes(-2), ExpiresAt = DateTime.UtcNow.AddMinutes(8)
    };

    private static async Task<OtpService> CreateServiceAsync(HoponDbContext db,
        string environment = "Development", bool expose = true, CapturingLogger? logger = null)
    {
        db.Users.Add(new User { PhoneNumber = Phone, FullName = "OTP test passenger" });
        await db.SaveChangesAsync();
        return new OtpService(db, new TestEnvironment { EnvironmentName = environment },
            logger ?? new CapturingLogger(), Options.Create(new OtpOptions { ExposeCodeInResponse = expose }));
    }

    private sealed class CapturingLogger : ILogger<OtpService>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Hopon.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
