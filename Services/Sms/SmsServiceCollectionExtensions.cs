using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Http;

namespace Hopon.Api.Services.Sms;

public static class SmsServiceCollectionExtensions
{
    public static IServiceCollection AddSmsSending(this IServiceCollection services)
    {
        services.AddOptions<TwilioOptions>().BindConfiguration("Twilio");
        services.AddOptions<SmsOptions>().BindConfiguration("Sms")
            .Validate(o => o.UsesTwilio || string.Equals(o.Provider, "Logging", StringComparison.OrdinalIgnoreCase),
                "Sms:Provider must be Logging or Twilio.")
            .Validate<IOptions<TwilioOptions>>((sms, twilio) => !sms.UsesTwilio || twilio.Value.HasCredentials,
                "Sms:Provider is Twilio: Twilio:AccountSid and Twilio:AuthToken are required.")
            .Validate<IOptions<TwilioOptions>>((sms, twilio) => !sms.UsesTwilio || twilio.Value.HasSender,
                "Sms:Provider is Twilio: set Twilio:FromNumber or Twilio:MessagingServiceSid.").ValidateOnStart();

        services.AddHttpClient("TwilioSms", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<ITwilioRestClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("TwilioSms");
            // Disable SDK debug logging too: it can print the OTP request body.
            return new TwilioRestClient(options.AccountSid, options.AuthToken, httpClient: new SystemNetHttpClient(http))
            { LogLevel = "none" };
        });
        services.AddScoped<LoggingSmsSender>();
        services.AddScoped<TwilioSmsSender>();
        services.AddScoped<ISmsSender>(provider => provider.GetRequiredService<IOptions<SmsOptions>>().Value.UsesTwilio
            ? provider.GetRequiredService<TwilioSmsSender>() : provider.GetRequiredService<LoggingSmsSender>());
        return services;
    }
}
