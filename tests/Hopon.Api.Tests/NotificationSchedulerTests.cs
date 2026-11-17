using System.Data.Common;
using System.Net.Http.Headers;
using System.Text.Json;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class NotificationSchedulerTests
{
    [Fact]
    public async Task Scheduler_arrival_and_delay_share_sms_policy_and_do_not_repeat_reminder_or_arrival_sms()
    {
        await using var fixture = await SchedulerFixture.CreateAsync(smsEnabled: true);
        var sender = (FakeSmsSender)fixture.Services.GetRequiredService<Hopon.Api.Services.Sms.ISmsSender>();
        fixture.Clock.Advance(TimeSpan.FromMinutes(270));
        await fixture.Scheduler.RunOnceAsync();
        await fixture.Scheduler.RunOnceAsync();
        Assert.Contains("Harrismith", Assert.Single(sender.Messages).Message);
        var logs = await fixture.Logs();
        Assert.Equal(2, logs.Count(n => n.Type == NotificationType.ThirtyMinutesBefore));
        Assert.Equal(NotificationChannel.InApp, Assert.Single(logs, n => n.Type == NotificationType.TwoHoursBefore).Channel);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var driver = scope.ServiceProvider.GetRequiredService<IDriverTripService>();
        var trip = (await db.Trips.FindAsync(fixture.TripId))!;
        Assert.True((await driver.MarkStopArrivedAsync(trip, fixture.FirstStopId)).Success);
        Assert.False((await driver.MarkStopArrivedAsync(trip, fixture.FirstStopId)).Success);
        Assert.Equal(2, (await fixture.Logs()).Count(n => n.Type == NotificationType.Arrival && n.Channel == NotificationChannel.Sms));
        Assert.True((await driver.ReportDelayedAsync(trip, "Traffic")).Success);
        Assert.Contains(sender.Messages, m => m.Message.Contains("Traffic"));
    }

    [Fact]
    public async Task Scheduled_notification_is_visible_only_in_its_passengers_api_feed()
    {
        using var baseApp = new StaffApplication();
        var clock = new FakeTimeProvider(SchedulerFixture.Start);
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = app.CreateClient();
        string token;
        string otherToken;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = (await db.Trips.FindAsync(101))!;
            trip.Status = TripStatus.InProgress;
            trip.ScheduledDeparture = clock.GetUtcNow().UtcDateTime.AddHours(-1);
            trip.ActualDeparture = trip.ScheduledDeparture;
            (await db.TripStops.FindAsync(101))!.ScheduledArrival = clock.GetUtcNow().UtcDateTime.AddMinutes(30);
            db.Tickets.Add(new Ticket { UserId = 1, TripId = 101, DestinationTripStopId = 101, TicketReference = "FEED-TEST" });
            await db.SaveChangesAsync();
            var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            token = tokens.GenerateToken((await db.Users.FindAsync(1))!).Token;
            otherToken = tokens.GenerateToken((await db.Users.FindAsync(2))!).Token;
        }
        var scheduler = app.Services.GetServices<IHostedService>().OfType<NotificationSchedulerService>().Single();
        await scheduler.RunOnceAsync();
        await scheduler.RunOnceAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/notifications/my"));
        var notification = Assert.Single(feed.RootElement.EnumerateArray());
        Assert.Equal("ThirtyMinutesBefore", notification.GetProperty("type").GetString());
        Assert.Contains("Test stop 101", notification.GetProperty("message").GetString());
        Assert.EndsWith("Z", notification.GetProperty("sentAt").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        Assert.Equal("[]", await client.GetStringAsync("/api/notifications/my"));
    }

    [Fact]
    public async Task Advancing_fake_time_creates_each_type_once_per_passenger_then_driver_arrival_and_delay_still_work()
    {
        await using var fixture = await SchedulerFixture.CreateAsync();
        await fixture.Scheduler.RunOnceAsync();
        Assert.Empty(await fixture.Logs());
        var elapsed = 0;
        foreach (var minutes in new[] { 60, 120, 180, 240, 270, 290, 330, 350 })
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(minutes - elapsed));
            elapsed = minutes;
            var readsBefore = fixture.Interceptor.Selects;
            await fixture.Scheduler.RunOnceAsync();
            Assert.Equal(2, fixture.Interceptor.Selects - readsBefore);
            await fixture.Scheduler.RunOnceAsync(); // The same tick must not send twice.
        }

        var logs = await fixture.Logs();
        Assert.Equal(8, logs.Count);
        foreach (var userId in new[] { 1, 2 })
        {
            var personal = logs.Where(n => n.UserId == userId).OrderBy(n => n.Type).ToList();
            Assert.Equal(new[] { NotificationType.FourHoursBefore, NotificationType.TwoHoursBefore,
                NotificationType.ThirtyMinutesBefore, NotificationType.TenMinutesBefore }, personal.Select(n => n.Type));
            var eta = SchedulerFixture.Start.AddHours(userId == 1 ? 5 : 6).UtcDateTime;
            foreach (var (type, minutes) in new[] { (NotificationType.FourHoursBefore, 240), (NotificationType.TwoHoursBefore, 120),
                (NotificationType.ThirtyMinutesBefore, 30), (NotificationType.TenMinutesBefore, 10) })
                Assert.Equal(eta.AddMinutes(-minutes), personal.Single(n => n.Type == type).SentAt);
            Assert.All(personal, n =>
            {
                Assert.Equal(NotificationChannel.InApp, n.Channel);
                Assert.Equal(NotificationStatus.Sent, n.Status);
                Assert.Equal(n.SentAt, n.CreatedAt);
                Assert.Contains(userId == 1 ? "Harrismith" : "Johannesburg Park Station", n.Message);
                Assert.Contains(userId == 1 ? "19:00" : "20:00", n.Message);
            });
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var driver = scope.ServiceProvider.GetRequiredService<IDriverTripService>();
            var trip = (await db.Trips.FindAsync(fixture.TripId))!;
            Assert.True((await driver.MarkStopArrivedAsync(trip, fixture.FirstStopId)).Success);
            Assert.True((await driver.MarkStopArrivedAsync(trip, fixture.SecondStopId)).Success);
            Assert.False((await driver.MarkStopArrivedAsync(trip, fixture.FirstStopId)).Success);
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.NotifyStopArrivedAsync((await db.TripStops.Include(s => s.Stop).SingleAsync(s => s.Id == fixture.FirstStopId)));
            var arrivals = (await fixture.Logs()).Where(n => n.Type == NotificationType.Arrival).ToList();
            Assert.Equal(new[] { 1, 2, 5 }, arrivals.Select(n => n.UserId).OrderBy(id => id));
            Assert.All(arrivals, n => Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime, n.SentAt));
            Assert.Equal("You have arrived at Harrismith.", arrivals.Single(n => n.UserId == 1).Message);
            Assert.Equal("You have arrived at Johannesburg Park Station.", arrivals.Single(n => n.UserId == 2).Message);

            Assert.True((await driver.ReportDelayedAsync(trip, "Traffic")).Success);
            Assert.True((await driver.ReportDelayedAsync(trip, "Roadworks")).Success);
            Assert.Equal(2, (await fixture.Logs()).Count(n => n.UserId == 1 && n.Type == NotificationType.Delay));
            Assert.Contains(await notifications.GetNotificationsForUserAsync(1), n => n.Type == "Arrival");
        }
        await fixture.Scheduler.RunOnceAsync();
        Assert.Equal(8, (await fixture.Logs()).Count(n => n.Type <= NotificationType.TenMinutesBefore));
    }

    [Theory]
    [InlineData("Development", 270, true)]
    [InlineData("Production", 270, false)]
    [InlineData("Staging", 270, false)]
    [InlineData("Development", 0, false)]
    public async Task Demo_offset_is_used_only_in_development(string environment, int offset, bool shouldNotify)
    {
        await using var fixture = await SchedulerFixture.CreateAsync(environment, offset);
        await fixture.Scheduler.RunOnceAsync();
        var logs = await fixture.Logs();
        Assert.Equal(shouldNotify ? 2 : 0, logs.Count);
        if (shouldNotify)
        {
            Assert.Equal(NotificationType.ThirtyMinutesBefore, logs.Single(n => n.UserId == 1).Type);
            Assert.Equal(NotificationType.TwoHoursBefore, logs.Single(n => n.UserId == 2).Type);
            Assert.All(logs, n => Assert.Equal(SchedulerFixture.Start.UtcDateTime, n.SentAt));
            await fixture.Scheduler.RunOnceAsync();
            Assert.Equal(2, (await fixture.Logs()).Count);
        }
    }

    [Theory]
    [InlineData(TripStatus.Scheduled, 0)]
    [InlineData(TripStatus.Completed, 0)]
    [InlineData(TripStatus.Cancelled, 0)]
    [InlineData(TripStatus.InProgress, 2)]
    [InlineData(TripStatus.Delayed, 2)]
    public async Task Only_running_or_delayed_trips_are_processed(TripStatus status, int expected)
    {
        await using var fixture = await SchedulerFixture.CreateAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            (await db.Trips.FindAsync(fixture.TripId))!.Status = status;
            await db.SaveChangesAsync();
        }
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.Scheduler.RunOnceAsync();
        Assert.Equal(expected, (await fixture.Logs()).Count);
    }

    [Fact]
    public async Task One_failed_ticket_does_not_block_other_tickets_and_retries_next_tick()
    {
        await using var fixture = await SchedulerFixture.CreateAsync();
        fixture.Interceptor.FailFirstPassengerOnce = true;
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.Scheduler.RunOnceAsync();
        Assert.Equal(2, Assert.Single(await fixture.Logs()).UserId);
        await fixture.Scheduler.RunOnceAsync();
        Assert.Equal(2, (await fixture.Logs()).Count);
    }

    [Fact]
    public async Task Unique_index_rejects_a_racing_insert_and_writer_can_continue()
    {
        await using var fixture = await SchedulerFixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<PassengerNotificationWriter>();
        NotificationLog Log(int userId) => new() { UserId = userId, TripId = fixture.TripId,
            Type = NotificationType.Arrival, Channel = NotificationChannel.InApp, Status = NotificationStatus.Sent };
        Assert.True(await writer.TryWriteAsync(Log(1)));
        var duplicate = Log(1); // Models two workers that both saw an empty batch of existing logs.
        Assert.False(await writer.TryWriteAsync(duplicate));
        Assert.Equal(EntityState.Detached, db.Entry(duplicate).State);
        Assert.True(await writer.TryWriteAsync(Log(2)));
        Assert.Equal(2, (await fixture.Logs()).Count);
    }

    [Fact]
    public async Task Hosted_timer_uses_fake_time_and_shuts_down_cleanly()
    {
        await using var fixture = await SchedulerFixture.CreateAsync();
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await fixture.Scheduler.StartAsync(CancellationToken.None);
        try
        {
            Assert.Empty(await fixture.Logs());
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            await fixture.Interceptor.NotificationSaved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await fixture.Scheduler.StopAsync(CancellationToken.None);
        }
        Assert.True(fixture.Scheduler.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Single(await fixture.Logs());
    }

    private sealed class SchedulerFixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        private readonly SqliteConnection _connection;
        public ServiceProvider Services { get; }
        public FakeTimeProvider Clock { get; } = new(Start);
        public QueryAndFailureInterceptor Interceptor { get; } = new();
        public NotificationSchedulerService Scheduler => Services.GetRequiredService<NotificationSchedulerService>();
        public int TripId { get; private set; }
        public int FirstStopId { get; private set; }
        public int SecondStopId { get; private set; }

        private SchedulerFixture(SqliteConnection connection, string environment, int offset, bool smsEnabled)
        {
            _connection = connection;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSignalR();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Demo:ClockOffsetMinutes"] = offset.ToString() }).Build());
            services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
            services.AddDbContext<HoponDbContext>(options => options.UseSqlite(connection).AddInterceptors(Interceptor));
            services.AddScoped<PassengerNotificationWriter>();
            services.AddSingleton<Hopon.Api.Services.Sms.ISmsSender>(new FakeSmsSender());
            services.AddOptions<NotificationOptions>().Configure(o => o.SmsEnabled = smsEnabled);
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IDriverTripService, DriverTripService>();
            services.AddSingleton<NotificationSchedulerService>();
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public static async Task<SchedulerFixture> CreateAsync(string environment = "Development", int offset = 0, bool smsEnabled = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new SchedulerFixture(connection, environment, offset, smsEnabled);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            await db.Database.EnsureCreatedAsync();
            for (var id = 4; id <= 9; id++)
                db.Users.Add(new User { Id = id, FullName = "Test passenger " + id, PhoneNumber = "+2778000010" + id });
            var trip = new Trip
            {
                Bus = new Bus { RegistrationNumber = "SCHEDULER" }, BusRoute = new BusRoute { Name = "Scheduler route" },
                Status = TripStatus.InProgress, ScheduledDeparture = Start.AddHours(-2).UtcDateTime,
                ActualDeparture = Start.AddHours(-2).AddMinutes(10).UtcDateTime, ScheduledArrival = Start.AddHours(8).UtcDateTime
            };
            TripStop Stop(string name, int order, double hours, TripStopStatus status = TripStopStatus.Pending) => new()
            {
                Stop = new Stop { Name = name }, SequenceOrder = order, Status = status,
                ScheduledArrival = Start.AddHours(hours).AddMinutes(-10).UtcDateTime
            };
            var first = Stop("Harrismith", 1, 5);
            var second = Stop("Johannesburg Park Station", 2, 6);
            var arrived = Stop("Already arrived", 3, 3, TripStopStatus.Arrived);
            var departed = Stop("Already departed", 4, 3, TripStopStatus.Departed);
            var skipped = Stop("Skipped", 5, 3, TripStopStatus.Skipped);
            var past = Stop("Past ETA", 6, -1);
            trip.TripStops = new List<TripStop> { first, second, arrived, departed, skipped, past };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            Ticket Ticket(int userId, TripStop? destination, bool active = true) => new()
            {
                UserId = userId, TripId = trip.Id, DestinationTripStopId = destination?.Id,
                TicketReference = "SCHEDULER-" + userId, IsActive = active
            };
            var alighted = Ticket(5, first);
            alighted.BoardingLogs.Add(new BoardingLog { EventType = BoardingEventType.Alighted });
            db.Tickets.AddRange(Ticket(1, first), Ticket(2, second), Ticket(3, null), Ticket(4, first, false),
                alighted, Ticket(6, arrived), Ticket(7, departed), Ticket(8, skipped), Ticket(9, past));
            await db.SaveChangesAsync();
            fixture.TripId = trip.Id;
            fixture.FirstStopId = first.Id;
            fixture.SecondStopId = second.Id;
            return fixture;
        }

        public async Task<List<NotificationLog>> Logs()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<HoponDbContext>().NotificationLogs.AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class QueryAndFailureInterceptor : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public int Selects { get; private set; }
        public bool FailFirstPassengerOnce { get; set; }
        public TaskCompletionSource<bool> NotificationSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Selects++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailFirstPassengerOnce && eventData.Context!.ChangeTracker.Entries<NotificationLog>()
                .Any(e => e.State == EntityState.Added && e.Entity.UserId == 1))
            {
                FailFirstPassengerOnce = false;
                throw new DbUpdateException("Test-only insert failure.");
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<NotificationLog>().Any())
                NotificationSaved.TrySetResult(true);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Hopon.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
