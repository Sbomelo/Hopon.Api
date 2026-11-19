using System.Net.Http.Headers;
using System.Text.Json;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Hopon.Api.Services.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class EmergencyAlertTests
{
    [Fact]
    public async Task Arrival_toggle_then_completion_sends_once_to_contact_and_logs_for_passenger()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Driver.MarkStopArrivedAsync(fixture.Trip, fixture.Last.Id)).Success);
        fixture.Last.Status = TripStopStatus.Pending;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Driver.MarkStopArrivedAsync(fixture.Trip, fixture.Last.Id)).Success);
        Assert.True((await fixture.Driver.CompletedAsync(fixture.Trip)).Success);

        Assert.Equal(("+27820000001", "Hopon: Thandi has arrived safely at Johannesburg Park Station."),
            Assert.Single(fixture.Sender.Messages));
        var log = Assert.Single(await fixture.EmergencyLogs());
        Assert.Equal(1, log.UserId);
        Assert.Equal(fixture.Trip.Id, log.TripId);
        Assert.Equal(NotificationChannel.Sms, log.Channel);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime, log.SentAt);
        Assert.Equal("We told Mom you arrived.", log.Message);
        Assert.DoesNotContain("+27", log.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_contact_or_voided_ticket_never_sends(bool voided)
    {
        await using var fixture = await Fixture.CreateAsync(contact: voided);
        if (voided)
        {
            (await fixture.Db.Tickets.SingleAsync()).IsActive = false;
            await fixture.Db.SaveChangesAsync();
        }
        await fixture.Driver.MarkStopArrivedAsync(fixture.Trip, fixture.Last.Id);
        await fixture.Driver.CompletedAsync(fixture.Trip);
        Assert.Empty(fixture.Sender.Messages);
        Assert.Empty(await fixture.EmergencyLogs());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_failure_or_exception_is_recorded_and_never_retried(bool throws)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Sender.Throw = throws;
        fixture.Sender.Result = new(false, "Provider error containing +27820000001", null);
        Assert.True((await fixture.Driver.MarkStopArrivedAsync(fixture.Trip, fixture.Last.Id)).Success);
        Assert.True((await fixture.Driver.CompletedAsync(fixture.Trip)).Success);
        await fixture.Notifications.NotifyStopArrivedAsync(fixture.Last);
        Assert.Single(fixture.Sender.Messages);
        var log = Assert.Single(await fixture.EmergencyLogs());
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Null(log.SentAt);
        Assert.Contains("Mom", log.Message);
        Assert.DoesNotContain("+27", log.Message);
        Assert.DoesNotContain("We told", log.Message);
    }

    [Fact]
    public async Task Completion_without_arrival_alerts_only_final_stop_tickets_and_preserves_stop_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.EmergencyContacts.Add(new EmergencyContact { UserId = 2, Name = "Dad", PhoneNumber = "+27820000002" });
        fixture.Db.Tickets.Add(new Ticket { UserId = 2, TripId = fixture.Trip.Id,
            DestinationTripStopId = fixture.First.Id, TicketReference = "INTERMEDIATE" });
        fixture.Db.Tickets.Add(new Ticket { UserId = 3, TripId = fixture.Trip.Id, TicketReference = "LEGACY" });
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Driver.CompletedAsync(fixture.Trip)).Success);
        await fixture.Notifications.NotifyTripCompletedAsync(fixture.Trip);
        Assert.Single(fixture.Sender.Messages);
        Assert.Equal(1, Assert.Single(await fixture.EmergencyLogs()).UserId);
        Assert.Equal(TripStopStatus.Pending, fixture.Last.Status);
        Assert.Null(fixture.Last.ActualArrival);
    }

    [Theory]
    [InlineData(false, false, false, "SMS disabled")]
    [InlineData(true, false, false, "SMS disabled")]
    [InlineData(true, true, true, "SMS simulated")]
    public async Task Disabled_filtered_or_simulated_sms_stays_pending_without_claiming_delivery(
        bool enabled, bool allowEmergency, bool simulated, string message)
    {
        await using var fixture = await Fixture.CreateAsync(enabled: enabled, allowEmergency: allowEmergency);
        fixture.Sender.Result = new(true, null, null) { IsSimulated = simulated };
        await fixture.Driver.MarkStopArrivedAsync(fixture.Trip, fixture.Last.Id);
        await fixture.Driver.CompletedAsync(fixture.Trip);
        Assert.Equal(simulated ? 1 : 0, fixture.Sender.Messages.Count);
        var log = Assert.Single(await fixture.EmergencyLogs());
        Assert.Equal(NotificationStatus.Pending, log.Status);
        Assert.Null(log.SentAt);
        Assert.Contains(message, log.Message);
        Assert.Contains("Mom", log.Message);
        Assert.DoesNotContain("We told", log.Message);
    }

    [Fact]
    public async Task Second_scope_cannot_send_while_first_sender_is_in_flight()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Sender.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Notifications.NotifyTripCompletedAsync(new Trip { Id = fixture.Trip.Id, Status = TripStatus.Completed });
        await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var secondDb = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>()
            .UseSqlite(fixture.Db.Database.GetDbConnection()).Options);
        var second = fixture.Service(secondDb);
        await second.NotifyTripCompletedAsync(new Trip { Id = fixture.Trip.Id, Status = TripStatus.Completed });
        fixture.Sender.Release.SetResult(true);
        await first;
        Assert.Single(fixture.Sender.Messages);
        Assert.Single(await fixture.EmergencyLogs());
    }

    [Theory]
    [InlineData(NotificationChannel.Sms, NotificationStatus.Sent)]
    [InlineData(NotificationChannel.InApp, NotificationStatus.Failed)]
    [InlineData(NotificationChannel.Sms, NotificationStatus.Pending)]
    public async Task Database_rejects_duplicate_emergency_across_channels_and_statuses(NotificationChannel channel, NotificationStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var writer = new PassengerNotificationWriter(fixture.Db, NullLogger<PassengerNotificationWriter>.Instance);
        Assert.True(await writer.TryWriteAsync(new NotificationLog { UserId = 1, TripId = fixture.Trip.Id,
            Type = NotificationType.Emergency, Channel = NotificationChannel.Sms }));
        Assert.False(await writer.TryWriteAsync(new NotificationLog { UserId = 1, TripId = fixture.Trip.Id,
            Type = NotificationType.Emergency, Channel = channel, Status = status }));
        Assert.Single(await fixture.EmergencyLogs());
    }

    [Fact]
    public async Task Emergency_result_appears_only_in_passengers_authenticated_feed()
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:SmsEnabled"] = "true" })));
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.EmergencyContacts.Add(new EmergencyContact { UserId = 1, Name = "Mom", PhoneNumber = "+27820000001" });
        db.Tickets.Add(new Ticket { UserId = 1, TripId = 101, DestinationTripStopId = 101, TicketReference = "EMERGENCY-FEED" });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .NotifyStopArrivedAsync(await db.TripStops.Include(s => s.Stop).SingleAsync(s => s.Id == 101));
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(1))!).Token);
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/notifications/my"));
        var alert = Assert.Single(feed.RootElement.EnumerateArray(), item => item.GetProperty("type").GetString() == "Emergency");
        Assert.Equal("We told Mom you arrived.", alert.GetProperty("message").GetString());
        Assert.Equal("Sent", alert.GetProperty("status").GetString());
        Assert.DoesNotContain("+27820000001", alert.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken((await db.Users.FindAsync(2))!).Token);
        Assert.Equal("[]", await client.GetStringAsync("/api/notifications/my"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestDb _database;
        private readonly ServiceProvider _services;
        private readonly NotificationOptions _options;
        public HoponDbContext Db => _database.Context;
        public RecordingSender Sender { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        public Trip Trip { get; }
        public TripStop First => Trip.TripStops.Single(s => s.SequenceOrder == 1);
        public TripStop Last => Trip.TripStops.Single(s => s.SequenceOrder == 2);
        public NotificationService Notifications => Service(Db);
        public ITripLifecycleService Driver => new TripLifecycleService(Db, NullLogger<TripLifecycleService>.Instance,
            _services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hopon.Api.Hubs.TripHub>>(), Notifications, new LocationThrottle(Clock), Clock);

        private Fixture(TestDb database, NotificationOptions options)
        {
            _database = database;
            _options = options;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSignalR();
            _services = services.BuildServiceProvider();
            Trip = new Trip { Bus = new Bus { RegistrationNumber = "EMERGENCY" }, BusRoute = new BusRoute { Name = "Private route" },
                Status = TripStatus.InProgress, TripStops = new List<TripStop> {
                    new() { SequenceOrder = 1, Stop = new Stop { Name = "Harrismith" } },
                    new() { SequenceOrder = 2, Stop = new Stop { Name = "Johannesburg Park Station" } } } };
        }

        public NotificationService Service(HoponDbContext db) => new(db,
            new PassengerNotificationWriter(db, NullLogger<PassengerNotificationWriter>.Instance), Clock, Sender, Options.Create(_options));

        public static async Task<Fixture> CreateAsync(bool contact = true, bool enabled = true, bool allowEmergency = true)
        {
            var fixture = new Fixture(await TestDb.CreateAsync(), new NotificationOptions { SmsEnabled = enabled,
                SmsTypes = allowEmergency ? ["Emergency"] : [] });
            (await fixture.Db.Users.FindAsync(1))!.FullName = "  Thandi\tPrivate Surname  ";
            fixture.Db.Trips.Add(fixture.Trip);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.Tickets.Add(new Ticket { UserId = 1, TripId = fixture.Trip.Id,
                DestinationTripStopId = fixture.Last.Id, TicketReference = "EMERGENCY" });
            if (contact) fixture.Db.EmergencyContacts.Add(new EmergencyContact { UserId = 1, Name = "Mom", PhoneNumber = "+27820000001" });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public Task<List<NotificationLog>> EmergencyLogs() => Db.NotificationLogs.AsNoTracking()
            .Where(n => n.Type == NotificationType.Emergency).ToListAsync();

        public async ValueTask DisposeAsync() { await _services.DisposeAsync(); await _database.DisposeAsync(); }
    }

    private sealed class RecordingSender : ISmsSender
    {
        public List<(string To, string Message)> Messages { get; } = new();
        public SmsSendResult Result { get; set; } = new(true, null, "test-message");
        public bool Throw { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? Release { get; set; }
        public async Task<SmsSendResult> SendAsync(string toE164, string message, CancellationToken ct = default)
        {
            Messages.Add((toE164, message));
            Started.TrySetResult(true);
            if (Release != null) await Release.Task;
            if (Throw) throw new InvalidOperationException("Provider error containing " + toE164);
            return Result;
        }
    }
}
