using Hopon.Api.Data;
using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services;

public class OtpService : IOtpService
{
    private const int OtpLenghtDigits = 6;
    private const int OtpExpiryMinutes = 10;
    private const int MaxRequestsPerWindow = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);
    private const int MaxVerifyAttempts = 5;


    private readonly HoponDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OtpService> _logger;

    public OtpService(HoponDbContext db, IWebHostEnvironment env, ILogger<OtpService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error, string? DevOnlyCode)> RequestOtpAsync (string phoneNumber)
    {

        //Check if user eexist
        var userExists = await _db.Users.AnyAsync( u => u.PhoneNumber == phoneNumber);
        
        if (!userExists)
        {
            return (false, "Unable to process this request.", null);

        }

        var windowStart = DateTime.UtcNow - RateLimitWindow;
        var recentRequestCount = await _db.OtpRequests  
                                     .CountAsync(o => o.PhoneNumber== phoneNumber && o.CreatedAt >= windowStart);
    

        if(recentRequestCount >= MaxRequestsPerWindow)
        {
            return (false, "Too many OTP requests. Please try again later.", null);
        }

        //Genrate code and hash it
        var code = GenerateNumericCode(OtpLenghtDigits);
        var hash = BCrypt.Net.BCrypt.HashPassword(code);

        //RequestCode object
        var otpRequest = new OtpRequest
        {
            PhoneNumber = phoneNumber,
            OtpCodeHash = hash,
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
            IsUsed = false,
            AttemptCount =0
        };

        _db.OtpRequests.Add(otpRequest);
        await _db.SaveChangesAsync();

        //TODO SMS FEATURE : Will send via Twilio instead of logging.
        _logger.LogInformation("OTP for {PhoneNumber} : {Code}", phoneNumber, code);

        //Only return the raw code outside of development
        var devOnlyCode = _env.IsDevelopment() ? code : null;

        return (true, null, devOnlyCode);
    }

    public async Task<(bool Success, string? Error)> VerifyOtpAsync (string phoneNumber, string code)
    {
        var otpRequest = await _db.OtpRequests
                                .Where(o => o.PhoneNumber == phoneNumber && !o.IsUsed)
                                .OrderByDescending( o => o.CreatedAt)
                                .FirstOrDefaultAsync();
        

        if (otpRequest is null)
        {
            return (false, "No active OTP request found. Please request an OTP code.");
        }


        if(otpRequest.ExpiresAt < DateTime.UtcNow)
        {
            return (false, "This code has expired. Please request a new one");
        }

        
        if(otpRequest.AttemptCount >= MaxVerifyAttempts)
        {
            return (false, "Too many incorrect attempts. Please request a new code.");
        }

        var isMatch = BCrypt.Net.BCrypt.Verify(code, otpRequest.OtpCodeHash);

        if (isMatch)
        {
            otpRequest.AttemptCount++;
            await _db.SaveChangesAsync();
            return (false, "Incorrect code");
        }

        otpRequest.IsUsed = true;
        await _db.SaveChangesAsync();

        return (true, null);
    }

    private static string GenerateNumericCode (int length)
    {
        var max = (int)Math.Pow(10, length);
        var code = Random.Shared.Next(0, max);
        return code.ToString(new string('O', length));
    }
}
    