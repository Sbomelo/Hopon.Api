using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Hopon.Api.Services;

public class OtpService : IOtpService
{
    private const int OtpExpiryMinutes = 10;
    private const int MaxRequestsPerWindow = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);
    private const int MaxVerifyAttempts = 5;

    private readonly HoponDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    public OtpService(HoponDbContext db, IHostEnvironment env, ILogger<OtpService> logger, IOptions<OtpOptions> options)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<(bool Success, string? Error, string? DevOnlyCode)> RequestOtpAsync(string phoneNumber)
    {
        var userExists = await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber && u.IsActive);

        if (!userExists)
            return (false, "Unable to process this request.", null);

        var now = DateTime.UtcNow;
        var windowStart = now - RateLimitWindow;
        var recentRequestCount = await _db.OtpRequests
            .CountAsync(o => o.PhoneNumber == phoneNumber && o.CreatedAt >= windowStart);

        if (recentRequestCount >= MaxRequestsPerWindow)
            return (false, "Too many OTP requests. Please try again later.", null);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = BCrypt.Net.BCrypt.HashPassword(code);

        var olderRequests = await _db.OtpRequests
            .Where(o => o.PhoneNumber == phoneNumber && !o.IsUsed)
            .ToListAsync();
        foreach (var olderRequest in olderRequests)
            olderRequest.IsUsed = true;

        var otpRequest = new OtpRequest
        {
            PhoneNumber = phoneNumber,
            OtpCodeHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpExpiryMinutes),
            IsUsed = false,
            AttemptCount = 0
        };

        _db.OtpRequests.Add(otpRequest);
        await _db.SaveChangesAsync();

        if (_env.IsDevelopment())
            _logger.LogInformation("OTP for {PhoneNumber} : {Code}", phoneNumber, code);

        var devOnlyCode = _options.ExposeCodeInResponse ? code : null;

        return (true, null, devOnlyCode);
    }

    public async Task<(bool Success, string? Error)> VerifyOtpAsync(string phoneNumber, string code)
    {
        if (!await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber && u.IsActive))
            return (false, "Unable to process this request.");

        var otpRequest = await _db.OtpRequests
            .Where(o => o.PhoneNumber == phoneNumber && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otpRequest is null)
            return (false, "No active OTP request found. Please request an OTP code.");

        if (otpRequest.ExpiresAt <= DateTime.UtcNow)
            return (false, "This code has expired. Please request a new one.");

        if (otpRequest.AttemptCount >= MaxVerifyAttempts)
            return (false, "Too many incorrect attempts. Please request a new code.");

        var isMatch = BCrypt.Net.BCrypt.Verify(code, otpRequest.OtpCodeHash);

        if (!isMatch)
        {
            otpRequest.AttemptCount++;
            await _db.SaveChangesAsync();
            return (false, "Incorrect code.");
        }

        otpRequest.IsUsed = true;
        await _db.SaveChangesAsync();

        return (true, null);
    }
}
