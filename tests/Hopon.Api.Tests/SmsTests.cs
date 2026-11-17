using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Hopon.Api.Services.Sms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class SmsTests
{
    [Theory]
    [InlineData(false, NotificationType.Arrival, 0)]
    [InlineData(true, NotificationType.Arrival, 1)]
    [InlineData(true, NotificationType.Delay, 1)]
    [InlineData(true, NotificationType.FourHoursBefore, 0)]
    [InlineData(true, NotificationType.TwoHoursBefore, 0)]
    [InlineData(true, NotificationType.ThirtyMinutesBefore, 1)]
    [InlineData(true, NotificationType.TenMinutesBefore, 1)]
    [InlineData(true, NotificationType.Emergency, 1)]
    public async Task Sms_is_sent_only_when_enabled_and_type_is_configured(bool enabled, NotificationType type, int expected)
    {
        await using var database = await TestDb.CreateAsync();
        var sender = new FakeSmsSender();
        var service = Service(database, sender, new NotificationOptions { SmsEnabled = enabled });
        await service.CreateAsync(1, null, type, "Test notification", [NotificationChannel.InApp, NotificationChannel.Sms]);
        Assert.Equal(expected, sender.Messages.Count);
        var logs = await database.Context.NotificationLogs.ToListAsync();
        Assert.Equal(1 + expected, logs.Count);
        Assert.All(logs, log => { Assert.Equal(NotificationStatus.Sent, log.Status); Assert.NotNull(log.SentAt); });
        if (expected == 1) Assert.Equal((await database.Context.Users.FindAsync(1))!.PhoneNumber, sender.Messages[0].To);
    }

    [Fact]
    public async Task Sms_failure_is_recorded_without_losing_the_inapp_notification()
    {
        await using var database = await TestDb.CreateAsync();
        var sender = new FakeSmsSender { Result = new(false, "test failure", null) };
        var service = Service(database, sender, new NotificationOptions { SmsEnabled = true });
        await service.CreateAsync(1, null, NotificationType.Arrival, "Test arrival", [NotificationChannel.InApp, NotificationChannel.Sms]);
        var logs = await database.Context.NotificationLogs.AsNoTracking().ToListAsync();
        var sms = Assert.Single(logs, l => l.Channel == NotificationChannel.Sms);
        Assert.Equal(NotificationStatus.Failed, sms.Status);
        Assert.Null(sms.SentAt);
        Assert.Equal(NotificationStatus.Sent, Assert.Single(logs, l => l.Channel == NotificationChannel.InApp).Status);
    }

    [Fact]
    public async Task Explicit_channel_selection_and_repeated_channels_are_respected()
    {
        await using var database = await TestDb.CreateAsync();
        var sender = new FakeSmsSender();
        var service = Service(database, sender, new NotificationOptions { SmsEnabled = true, SmsTypes = ["Arrival"] });
        await service.CreateAsync(1, null, NotificationType.Arrival, "Only in feed", [NotificationChannel.InApp]);
        await service.CreateAsync(1, null, NotificationType.Delay, "Not configured", [NotificationChannel.Sms]);
        await service.CreateAsync(1, null, NotificationType.Arrival, "Only SMS", [NotificationChannel.Sms, NotificationChannel.Sms]);
        Assert.Single(sender.Messages);
        Assert.Equal(2, await database.Context.NotificationLogs.CountAsync());
    }

    [Fact]
    public async Task Reminder_is_unique_per_channel_and_delay_can_repeat()
    {
        using var app = new StaffApplication();
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hopon.Api.Data.HoponDbContext>();
        var sender = new FakeSmsSender();
        var service = new NotificationService(db, new PassengerNotificationWriter(db, NullLogger<PassengerNotificationWriter>.Instance),
            TimeProvider.System, sender, Options.Create(new NotificationOptions { SmsEnabled = true }));
        for (var i = 0; i < 2; i++)
        {
            await service.CreateAsync(1, 101, NotificationType.TenMinutesBefore, "Reminder", [NotificationChannel.InApp, NotificationChannel.Sms]);
            await service.CreateAsync(1, 101, NotificationType.Delay, "Delay", [NotificationChannel.InApp, NotificationChannel.Sms]);
        }
        Assert.Equal(3, sender.Messages.Count);
        Assert.Equal(2, await db.NotificationLogs.CountAsync(l => l.Type == NotificationType.TenMinutesBefore));
        Assert.Equal(4, await db.NotificationLogs.CountAsync(l => l.Type == NotificationType.Delay));
    }

    [Theory]
    [InlineData("Twilio:AccountSid", "", "Twilio:AccountSid")]
    [InlineData("Twilio:AuthToken", "", "Twilio:AuthToken")]
    [InlineData("Twilio:FromNumber", "", "Twilio:FromNumber or Twilio:MessagingServiceSid")]
    [InlineData("Sms:Provider", "Other", "Sms:Provider")]
    [InlineData("Notifications:SmsTypes:0", "Typo", "Notifications:SmsTypes")]
    public void Invalid_sms_configuration_fails_at_startup(string key, string value, string expected)
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sms:Provider"] = "Twilio", ["Twilio:AccountSid"] = "test-sid", ["Twilio:AuthToken"] = "test-token",
                ["Twilio:FromNumber"] = "+27820000000", ["Twilio:MessagingServiceSid"] = "", [key] = value
            })));
        Assert.Contains(expected, Assert.Throws<OptionsValidationException>(() => app.CreateClient()).Message);
    }

    [Fact]
    public void Configured_sms_types_replace_defaults()
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:SmsEnabled"] = "true", ["Notifications:SmsTypes:0"] = "FourHoursBefore"
            })));
        using var client = app.CreateClient();
        var options = app.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
        Assert.True(options.SendsSms(NotificationType.FourHoursBefore));
        Assert.False(options.SendsSms(NotificationType.Arrival));
    }

    [Theory]
    [InlineData(null, typeof(LoggingSmsSender))]
    [InlineData("Logging", typeof(LoggingSmsSender))]
    [InlineData("Twilio", typeof(TwilioSmsSender))]
    public void Provider_selection_requires_no_network(string? provider, Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        var settings = new Dictionary<string, string?>();
        if (provider is not null)
            settings["Sms:Provider"] = provider;
        if (provider == "Twilio")
        {
            settings["Twilio:AccountSid"] = "test-sid";
            settings["Twilio:AuthToken"] = "test-token";
            settings["Twilio:MessagingServiceSid"] = "test-service";
        }
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSmsSending();
        using var container = services.BuildServiceProvider();
        using var scope = container.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<ISmsSender>());
        Assert.Equal(TimeSpan.FromSeconds(10), container.GetRequiredService<IHttpClientFactory>().CreateClient("TwilioSms").Timeout);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public async Task Logging_sender_never_logs_codes_outside_development(string environment, bool exposes)
    {
        var logger = new CapturingLogger<LoggingSmsSender>();
        var sender = new LoggingSmsSender(new TestEnvironment { EnvironmentName = environment }, logger);
        Assert.True((await sender.SendAsync("+27820000000", "Your Hopon code is 123456. It expires in 10 minutes.")).Success);
        var log = Assert.Single(logger.Messages);
        Assert.StartsWith("SMS to +27820000000 : ", log);
        Assert.Equal(exposes, log.Contains("123456"));
    }

    private static NotificationService Service(TestDb database, FakeSmsSender sender, NotificationOptions options) => new(
        database.Context, new PassengerNotificationWriter(database.Context, NullLogger<PassengerNotificationWriter>.Instance),
        new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)), sender, Options.Create(options));

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Hopon.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}
