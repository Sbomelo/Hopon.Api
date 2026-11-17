namespace Hopon.Api.Services.Sms;

public sealed class LoggingSmsSender(IHostEnvironment environment, ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new SmsSendResult(false, "SMS sending was cancelled.", null));

        // OTPs must never reach logs outside Development, even when Logging is selected.
        logger.LogInformation("SMS to {PhoneNumber} : {Message}", toE164,
            environment.IsDevelopment() ? message : "[message redacted outside Development]");
        return Task.FromResult(new SmsSendResult(true, null, null) { IsSimulated = true });
    }
}
