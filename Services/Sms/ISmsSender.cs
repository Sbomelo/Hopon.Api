namespace Hopon.Api.Services.Sms;

public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default);
}

public record SmsSendResult(bool Success, string? Error, string? ProviderMessageId);
