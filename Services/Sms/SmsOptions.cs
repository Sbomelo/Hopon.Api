namespace Hopon.Api.Services.Sms;

public sealed class SmsOptions
{
    public string Provider { get; set; } = "Logging";
    public bool UsesTwilio => string.Equals(Provider, "Twilio", StringComparison.OrdinalIgnoreCase);
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
    public string MessagingServiceSid { get; set; } = "";

    public bool HasCredentials => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
    public bool HasSender => !string.IsNullOrWhiteSpace(FromNumber) || !string.IsNullOrWhiteSpace(MessagingServiceSid);
}
