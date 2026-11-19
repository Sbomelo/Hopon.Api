using System.Net;
using Hopon.Api.Services.Sms;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Twilio.Clients;
using Twilio.Http;
using Xunit;

namespace Hopon.Api.Tests;

public class TwilioSmsSenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_serializes_the_chosen_sender_and_returns_provider_id_without_network(bool serviceSender)
    {
        var handler = new StubHandler();
        using var http = new System.Net.Http.HttpClient(handler);
        var sender = Sender(http, serviceSender);
        var result = await sender.SendAsync("+27820000000", "Your Hopon code is 123456. It expires in 10 minutes.");
        Assert.True(result.Success);
        Assert.Equal("SMtest", result.ProviderMessageId);
        Assert.Contains("To=%2B27820000000", handler.Body);
        Assert.Contains("Body=Your+Hopon+code+is+123456", handler.Body);
        Assert.Equal(serviceSender, handler.Body!.Contains("MessagingServiceSid=MGtest"));
        Assert.Equal(!serviceSender, handler.Body.Contains("From=%2B27820000001"));
    }

    [Fact]
    public async Task Provider_error_is_a_failed_result_and_never_logs_exception_text_or_otp()
    {
        var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":21608,"message":"secret OTP 123456","status":400}""")
        } };
        using var http = new System.Net.Http.HttpClient(handler);
        var logger = new CapturingLogger<TwilioSmsSender>();
        var result = await Sender(http, false, logger).SendAsync("+27820000000", "secret OTP 123456");
        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.DoesNotContain("123456", result.Error);
        var log = Assert.Single(logger.Messages);
        Assert.Contains("21608", log);
        Assert.DoesNotContain("123456", log);
    }

    [Fact]
    public async Task Failed_provider_response_and_transport_exception_are_failed_results()
    {
        var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"sid":"SMtest","status":"failed","error_code":30003}""")
        } };
        using var http = new System.Net.Http.HttpClient(handler);
        Assert.False((await Sender(http, false).SendAsync("+27820000000", "Test")).Success);
        handler.Throw = true;
        Assert.False((await Sender(http, false).SendAsync("+27820000000", "Test")).Success);
    }

    [Fact]
    public async Task Cancellation_and_ten_second_timeout_return_failure()
    {
        var handler = new StubHandler { WaitForever = true };
        using var http = new System.Net.Http.HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var clock = new FakeTimeProvider();
        var sender = Sender(http, false, clock: clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False((await sender.SendAsync("+27820000000", "Test", cancelled.Token)).Success);
        Assert.Null(handler.Body);
        var sending = sender.SendAsync("+27820000000", "Test");
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(sending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False((await sending).Success);
    }

    private static TwilioSmsSender Sender(System.Net.Http.HttpClient http, bool serviceSender,
        CapturingLogger<TwilioSmsSender>? logger = null, TimeProvider? clock = null)
    {
        var client = new TwilioRestClient("ACtest", "test-token", httpClient: new SystemNetHttpClient(http)) { LogLevel = "none" };
        return new TwilioSmsSender(client, Options.Create(new TwilioOptions
        {
            FromNumber = "+27820000001", MessagingServiceSid = serviceSender ? "MGtest" : ""
        }), logger ?? new CapturingLogger<TwilioSmsSender>(), clock ?? new FakeTimeProvider());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public bool Throw { get; set; }
        public bool WaitForever { get; set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"sid":"SMtest","status":"queued"}""")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Throw) throw new HttpRequestException("secret OTP 123456");
            if (WaitForever) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response;
        }
    }
}
