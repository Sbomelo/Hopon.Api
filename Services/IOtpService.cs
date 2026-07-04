namespace Hopon.Api.Services;

public interface IOtpService
{
    Task<(bool Success, string? Error, string? DevOnlyCode)> RequestOtpAsync (string phoneNumber);
    Task<(bool Success, string? Error)> VerifyOtpAsync (string phoneNumber, string code);
}