using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Hopon.Api.Services.Sms;

public sealed class TwilioSmsSender : ISmsSender
{
    private readonly ITwilioRestClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsSender> _logger;
    private readonly TimeProvider _clock;

    public TwilioSmsSender(ITwilioRestClient client, IOptions<TwilioOptions> options, ILogger<TwilioSmsSender> logger, TimeProvider clock)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var request = new CreateMessageOptions(new PhoneNumber(toE164)) { Body = message };
            if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
                request.MessagingServiceSid = _options.MessagingServiceSid;
            else
                request.From = new PhoneNumber(_options.FromNumber);

            var response = await MessageResource.CreateAsync(request, client: _client)
                .WaitAsync(TimeSpan.FromSeconds(10), _clock, ct);
            if (response.ErrorCode.HasValue || response.Status == MessageResource.StatusEnum.Failed
                || response.Status == MessageResource.StatusEnum.Undelivered)
            {
                _logger.LogWarning("Twilio rejected SMS with code {Code}.", response.ErrorCode);
                return new(false, "Twilio rejected the SMS.", response.Sid);
            }
            return new(true, null, response.Sid);
        }
        catch (Exception exception)
        {
            // Provider exceptions can contain request data. Never log their text or body.
            _logger.LogWarning("Twilio SMS failed ({FailureType}, code {Code}).",
                exception.GetType().Name, (exception as ApiException)?.Code);
            return new(false, "SMS sending failed.", null);
        }
    }
}
