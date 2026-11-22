using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Hopon.Api.Services.Sms;

namespace Hopon.Api.Services;

public class OtpService : IOtpService
{
    private const int OtpExpiryMinutes = 10;
    private const int MaxRequestsPerWindow = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);
    private const int MaxVerifyAttempts = 5;

    private readonly HoponDbContext _db;
    private readonly ISmsSender _smsSender;
    private readonly OtpOptions _options;
    private readonly TimeProvider _clock;

    public OtpService(HoponDbContext db, ISmsSender smsSender, IOptions<OtpOptions> options, TimeProvider clock)
    {
        _db = db;
        _smsSender = smsSender;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<(bool Success, string? Error, string? DevOnlyCode)> RequestOtpAsync(string phoneNumber)
    {
        var userExists = await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber && u.IsActive);

        if (!userExists)
            return (false, "Unable to process this request.", null);

        return await RequestCodeAsync(phoneNumber, null);
    }

    public async Task<(bool Success, string? Error, string? DevOnlyCode)> RequestPhoneChangeAsync(
        int userId, string currentPhone, string newPhone)
    {
        var user = await FindCurrentPassengerAsync(userId, currentPhone);
        if (user is null) return (false, "Sign in again to change your number.", null);

        var error = await CheckNewPhoneAsync(user, newPhone);
        if (error is not null) return (false, error, null);

        return await RequestCodeAsync(newPhone, userId);
    }

    private async Task<(bool Success, string? Error, string? DevOnlyCode)> RequestCodeAsync(
        string phoneNumber, int? phoneChangeUserId)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var windowStart = now - RateLimitWindow;
        var recentRequestCount = await _db.OtpRequests
            .CountAsync(o => o.PhoneNumber == phoneNumber && o.CreatedAt >= windowStart);

        if (recentRequestCount >= MaxRequestsPerWindow || (phoneChangeUserId.HasValue && await _db.OtpRequests
            .CountAsync(o => o.PhoneChangeUserId == phoneChangeUserId && o.CreatedAt >= windowStart) >= MaxRequestsPerWindow))
            return (false, "Too many OTP requests. Please try again later.", null);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = BCrypt.Net.BCrypt.HashPassword(code);

        var olderRequests = await _db.OtpRequests
            .Where(o => o.PhoneNumber == phoneNumber && o.PhoneChangeUserId == phoneChangeUserId && !o.IsUsed)
            .ToListAsync();
        foreach (var olderRequest in olderRequests)
            olderRequest.IsUsed = true;

        var otpRequest = new OtpRequest
        {
            PhoneNumber = phoneNumber,
            PhoneChangeUserId = phoneChangeUserId,
            OtpCodeHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpExpiryMinutes),
            IsUsed = false,
            AttemptCount = 0
        };

        _db.OtpRequests.Add(otpRequest);
        await _db.SaveChangesAsync();

        var purpose = phoneChangeUserId.HasValue ? "Hopon number change" : "Hopon";
        var sent = await _smsSender.SendAsync(phoneNumber, $"Your {purpose} code is {code}. It expires in 10 minutes.");
        if (!sent.Success)
        {
            _db.OtpRequests.Remove(otpRequest);
            // A failed delivery must not invalidate the passenger's previous working code.
            foreach (var olderRequest in olderRequests)
                olderRequest.IsUsed = false;
            await _db.SaveChangesAsync();
            return (false, "We couldn't send the SMS. Please try again.", null);
        }

        var devOnlyCode = _options.ExposeCodeInResponse ? code : null;

        return (true, null, devOnlyCode);
    }

    public async Task<(bool Success, string? Error)> VerifyOtpAsync(string phoneNumber, string code)
    {
        if (!await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber && u.IsActive))
            return (false, "Unable to process this request.");

        var (otpRequest, error) = await CheckCodeAsync(phoneNumber, null, code);
        if (otpRequest is null) return (false, error);

        otpRequest.IsUsed = true;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return (false, "This code has changed. Please try again."); }
        return (true, null);
    }

    public async Task<(User? User, string? Error)> ChangePhoneAsync(int userId, string currentPhone, string newPhone, string code)
    {
        var user = await FindCurrentPassengerAsync(userId, currentPhone);
        if (user is null) return (null, "Sign in again to change your number.");

        var availabilityError = await CheckNewPhoneAsync(user, newPhone);
        if (availabilityError is not null) return (null, availabilityError);

        var (otpRequest, error) = await CheckCodeAsync(newPhone, userId, code);
        if (otpRequest is null) return (null, error);

        var outstanding = await _db.OtpRequests.Where(o => !o.IsUsed && (o.PhoneChangeUserId == userId ||
            (o.PhoneChangeUserId == null && (o.PhoneNumber == currentPhone || o.PhoneNumber == newPhone))))
            .ToListAsync();
        foreach (var request in outstanding) request.IsUsed = true;
        otpRequest.IsUsed = true;
        user.PhoneNumber = newPhone;

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return (null, "Your number or code has changed. Please try again."); }
        catch (DbUpdateException)
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == newPhone && u.Id != userId))
                return (null, "That number is unavailable.");
            throw;
        }

        return (user, null);
    }

    private Task<User?> FindCurrentPassengerAsync(int userId, string currentPhone) =>
        _db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.PhoneNumber == currentPhone);

    private async Task<string?> CheckNewPhoneAsync(User user, string newPhone)
    {
        if (user.PhoneNumber == newPhone) return "Enter a different phone number.";
        if (await _db.Users.AnyAsync(u => u.PhoneNumber == newPhone)) return "That number is unavailable.";
        return null;
    }

    private async Task<(OtpRequest? Request, string? Error)> CheckCodeAsync(string phoneNumber, int? phoneChangeUserId, string code)
    {
        var otpRequest = await _db.OtpRequests
            .Where(o => o.PhoneNumber == phoneNumber && o.PhoneChangeUserId == phoneChangeUserId && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync();

        if (otpRequest is null)
            return (null, "No active OTP request found. Please request an OTP code.");

        if (otpRequest.ExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            return (null, "This code has expired. Please request a new one.");

        if (otpRequest.AttemptCount >= MaxVerifyAttempts)
            return (null, "Too many incorrect attempts. Please request a new code.");

        var isMatch = BCrypt.Net.BCrypt.Verify(code, otpRequest.OtpCodeHash);

        if (!isMatch)
        {
            otpRequest.AttemptCount++;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return (null, "This code has changed. Please try again."); }
            return (null, "Incorrect code.");
        }

        return (otpRequest, null);
    }
}
