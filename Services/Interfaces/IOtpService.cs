using Hopon.Api.Models;

namespace Hopon.Api.Services;

public interface IOtpService
{
    Task<(bool Success, string? Error, string? DevOnlyCode)> RequestOtpAsync (string phoneNumber);
    Task<(bool Success, string? Error)> VerifyOtpAsync (string phoneNumber, string code);
    Task<(bool Success, string? Error, string? DevOnlyCode)> RequestPhoneChangeAsync(int userId, string currentPhone, string newPhone);
    Task<(User? User, string? Error)> ChangePhoneAsync(int userId, string currentPhone, string newPhone, string code);
}
