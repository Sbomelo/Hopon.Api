using Hopon.Api.Services.Sms;

namespace Hopon.Api.Tests;

internal sealed class FakeSmsSender : ISmsSender
{
    public List<(string To, string Message)> Messages { get; } = new();
    public SmsSendResult Result { get; set; } = new(true, null, "test-message");

    public Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
    {
        Messages.Add((toE164, message));
        return Task.FromResult(Result);
    }
}
