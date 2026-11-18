using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.DTOs.RealTime;
using Hopon.Api.Hubs;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class TripLifecycleTests
{
    private static readonly DateTime Departure = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    private static bool Allowed(TripStatus from, TripStatus to) => (from, to) is
        (TripStatus.Scheduled, TripStatus.InProgress) or (TripStatus.Scheduled, TripStatus.Cancelled) or
        (TripStatus.InProgress, TripStatus.Delayed) or (TripStatus.InProgress, TripStatus.Completed) or
        (TripStatus.InProgress, TripStatus.Cancelled) or (TripStatus.Delayed, TripStatus.InProgress) or
        (TripStatus.Delayed, TripStatus.Completed) or (TripStatus.Delayed, TripStatus.Cancelled);

    public static IEnumerable<object[]> StatusPairs()
    {
        foreach (var from in Enum.GetValues<TripStatus>())
        foreach (var to in Enum.GetValues<TripStatus>())
            yield return [from, to, Allowed(from, to)];
    }

    public static IEnumerable<object[]> LifecyclePairs()
        => StatusPairs().Where(pair => (TripStatus)pair[1] != TripStatus.Scheduled);

    [Theory]
    [MemberData(nameof(StatusPairs))]
    public void Rules_allow_exactly_the_eight_transitions(TripStatus from, TripStatus to, bool allowed)
        => Assert.Equal(allowed, TripStatusRules.CanTransition(from, to));

    [Theory]
    [MemberData(nameof(LifecyclePairs))]
    public async Task Lifecycle_enforces_transitions_and_preserves_times_on_rejection(
        TripStatus from, TripStatus to, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync(from);
        var trip = fixture.Trip;
        var before = DateTime.UtcNow;
        var result = to switch
        {
            TripStatus.InProgress when from == TripStatus.Delayed => await fixture.Service.ResumeFromDelayAsync(trip),
            TripStatus.InProgress => await fixture.Service.StartTripAsync(trip),
            TripStatus.Delayed => await fixture.Service.ReportDelayedAsync(trip, "Traffic"),
            TripStatus.Completed => await fixture.Service.CompletedAsync(trip),
            TripStatus.Cancelled => await fixture.Service.CancelAsync(trip, "Road closed"),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(allowed, result.Success);
        if (!allowed)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Contains(from.ToString(), result.Error);
            Assert.Equal(from, trip.Status);
            Assert.Equal(Departure, trip.ActualDeparture);
            Assert.Null(trip.ActualArrival);
            Assert.Empty(fixture.Hub.Calls);
            Assert.Empty(await fixture.Db.NotificationLogs.ToListAsync());
            return;
        }

        Assert.Null(result.Error);
        fixture.Db.ChangeTracker.Clear();
        trip = (await fixture.Db.Trips.FindAsync(trip.Id))!;
        Assert.Equal(to, trip.Status);
        Assert.Equal(to.ToString(), Assert.Single(fixture.Hub.Calls).Payload.Status);
        if (from == TripStatus.Scheduled && to == TripStatus.InProgress)
            Assert.InRange(trip.ActualDeparture!.Value, before, DateTime.UtcNow);
        else
            Assert.Equal(Departure, trip.ActualDeparture);
        if (to == TripStatus.Completed)
            Assert.InRange(trip.ActualArrival!.Value, before, DateTime.UtcNow);
        else
            Assert.Null(trip.ActualArrival);
    }

    [Fact]
    public async Task Start_cannot_resume_a_delayed_trip_or_reset_its_departure()
    {
        await using var fixture = await Fixture.CreateAsync(TripStatus.Delayed);
        Assert.False((await fixture.Service.StartTripAsync(fixture.Trip)).Success);
        Assert.Equal(TripStatus.Delayed, fixture.Trip.Status);
        Assert.Equal(Departure, fixture.Trip.ActualDeparture);
        Assert.Empty(fixture.Hub.Calls);
    }

    [Theory]
    [InlineData(TripStatus.Scheduled)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.Cancelled)]
    public async Task Resume_rejects_every_state_except_delayed(TripStatus from)
    {
        await using var fixture = await Fixture.CreateAsync(from);
        Assert.False((await fixture.Service.ResumeFromDelayAsync(fixture.Trip)).Success);
        Assert.Equal(from, fixture.Trip.Status);
        Assert.Empty(fixture.Hub.Calls);
    }

    [Theory]
    [InlineData(null, "Your trip has been cancelled.")]
    [InlineData("  ", "Your trip has been cancelled.")]
    [InlineData(" Road closed ", "Your trip has been cancelled. Road closed")]
    public async Task Cancellation_notifies_each_active_holder_once_and_broadcasts(string? reason, string message)
    {
        await using var fixture = await Fixture.CreateAsync(TripStatus.Scheduled);
        Assert.True((await fixture.Service.CancelAsync(fixture.Trip, reason)).Success);
        Assert.False((await fixture.Service.CancelAsync(fixture.Trip, reason)).Success);
        var logs = await fixture.Db.NotificationLogs.OrderBy(n => n.UserId).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, logs.Select(n => n.UserId));
        Assert.All(logs, n =>
        {
            Assert.Equal(NotificationType.Cancellation, n.Type);
            Assert.Equal(NotificationChannel.InApp, n.Channel);
            Assert.Equal(NotificationStatus.Sent, n.Status);
            Assert.Equal(message, n.Message);
        });
        var call = Assert.Single(fixture.Hub.Calls);
        Assert.Equal(TripHub.GroupName(fixture.Trip.Id), call.Group);
        Assert.Equal("ReceiveTripStatusUpdate", call.Method);
        Assert.Equal(reason, call.Payload.Reason);
        Assert.Equal("Cancelled", call.Payload.Status);
        Assert.Equal(7, (int)NotificationType.Cancellation);
        Assert.Equal(6, (int)NotificationType.Emergency);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_sms_requires_explicit_policy_and_uses_one_attempt_per_holder(bool allowSms)
    {
        await using var fixture = await Fixture.CreateAsync(TripStatus.InProgress, allowSms);
        Assert.True((await fixture.Service.CancelAsync(fixture.Trip, null)).Success);
        Assert.False((await fixture.Service.CancelAsync(fixture.Trip, null)).Success);
        var logs = await fixture.Db.NotificationLogs.ToListAsync();
        Assert.Equal(allowSms ? 4 : 2, logs.Count);
        Assert.Equal(allowSms ? 2 : 0, fixture.Sender.Messages.Count);
        Assert.All(logs.GroupBy(n => new { n.UserId, n.Channel }), group => Assert.Single(group));
        Assert.All(fixture.Sender.Messages, sms => Assert.Equal("Your trip has been cancelled.", sms.Message));
    }

    [Fact]
    public async Task Admin_delay_and_driver_API_delay_have_identical_logs_and_broadcasts()
    {
        var adminResult = await DelayThroughHttp(admin: true);
        var driverResult = await DelayThroughHttp(admin: false);
        Assert.Equal(driverResult.Logs, adminResult.Logs);
        Assert.Equal(driverResult.Broadcast, adminResult.Broadcast);
    }

    private static async Task<(string Logs, string Broadcast)> DelayThroughHttp(bool admin)
    {
        using var baseApp = new StaffApplication();
        var hub = new HubSpy();
        using var app = WithHub(baseApp, hub);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = (await db.Trips.FindAsync(101))!;
            trip.Status = TripStatus.InProgress;
            trip.ActualDeparture = Departure;
            db.Tickets.AddRange(new Ticket { TripId = 101, UserId = 1, TicketReference = "ACTIVE" },
                new Ticket { TripId = 101, UserId = 2, TicketReference = "VOID", IsActive = false });
            await db.SaveChangesAsync();
            if (!admin)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Drivers.FindAsync(101))!).Token);
        }
        if (admin)
        {
            await LoginAdmin(client);
            Assert.Contains("Delay reason", await client.GetStringAsync("/Admin/Trips/Edit/101"));
            Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, "/Admin/Trips/Edit/101?handler=MarkDelayed",
                "/Admin/Trips/Edit/101", ("DelayReason", "Traffic"))).StatusCode);
        }
        else
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/driver/trips/101/delay", new { reason = "Traffic" })).StatusCode);

        using var finalScope = app.Services.CreateScope();
        var logs = await finalScope.ServiceProvider.GetRequiredService<HoponDbContext>().NotificationLogs
            .OrderBy(n => n.UserId).ThenBy(n => n.Channel)
            .Select(n => new { n.UserId, n.TripId, n.Type, n.Channel, n.Message, n.Status, n.CreatedAt, n.SentAt }).ToListAsync();
        Assert.Single(logs);
        Assert.Equal(NotificationType.Delay, logs[0].Type);
        Assert.Equal("Your bus has been delayed: Traffic", logs[0].Message);
        return (JsonSerializer.Serialize(logs), JsonSerializer.Serialize(Assert.Single(hub.Calls)));
    }

    [Fact]
    public async Task Admin_resume_completion_cancellation_errors_and_missing_ids_work_through_pages()
    {
        using var baseApp = new StaffApplication();
        var hub = new HubSpy();
        using var app = WithHub(baseApp, hub);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAdmin(client);
        const string edit = "/Admin/Trips/Edit/101";
        const string details = "/Admin/Trips/Details/101";
        // Invalid Scheduled -> Completed is rejected and the redirected page shows the service error.
        var rejected = await PostForm(client, edit + "?handler=MarkCompleted", edit);
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Contains("Cannot change trip status from Scheduled to Completed", await client.GetStringAsync(rejected.Headers.Location));
        Assert.Empty(hub.Calls);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var trip = (await db.Trips.FindAsync(101))!;
            trip.Status = TripStatus.Delayed;
            trip.ActualDeparture = Departure;
            db.EmergencyContacts.Add(new EmergencyContact { UserId = 1, Name = "Mom", PhoneNumber = "+27820000001" });
            db.Tickets.Add(new Ticket { TripId = 101, UserId = 1, DestinationTripStopId = 101, TicketReference = "FINAL" });
            await db.SaveChangesAsync();
        }
        Assert.Contains(">Resume</button>", await client.GetStringAsync(edit));
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, edit + "?handler=Resume", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, edit + "?handler=MarkCompleted", edit)).StatusCode);
        Assert.Equal(new[] { "InProgress", "Completed" }, hub.Calls.Select(c => c.Payload.Status));
        Assert.Equal(Departure, hub.Calls[0].Payload.ActualDeparture);
        Assert.NotNull(hub.Calls[1].Payload.ActualArrival);
        var invalidCancel = await PostForm(client, details + "?handler=Cancel", details);
        Assert.Contains("Cannot change trip status from Completed to Cancelled", await client.GetStringAsync(invalidCancel.Headers.Location));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            Assert.Equal(TripStatus.Completed, (await db.Trips.FindAsync(101))!.Status);
            Assert.Equal(NotificationType.Emergency, (await db.NotificationLogs.SingleAsync()).Type);
        }
        const string other = "/Admin/Trips/Details/102";
        Assert.Contains("Cancellation reason", await client.GetStringAsync(other));
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, other + "?handler=Cancel", other,
            ("CancellationReason", "Bus unavailable"))).StatusCode);
        Assert.Equal("Bus unavailable", hub.Calls.Last().Payload.Reason);
        Assert.Equal("Cancelled", hub.Calls.Last().Payload.Status);
        foreach (var handler in new[] { "MarkDelayed", "Resume", "MarkCompleted" })
            Assert.Equal(HttpStatusCode.NotFound, (await PostForm(client, "/Admin/Trips/Edit/999999?handler=" + handler, edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostForm(client, "/Admin/Trips/Details/999999?handler=Cancel", details)).StatusCode);
    }

    private static WebApplicationFactory<Program> WithHub(StaffApplication app, HubSpy hub)
        => app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHubContext<TripHub>>();
            services.AddSingleton<IHubContext<TripHub>>(hub);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(Departure)));
        }));

    private static async Task LoginAdmin(HttpClient client)
        => Assert.Equal(HttpStatusCode.Redirect, (await PostForm(client, "/Staff/Login", "/Staff/Login",
            ("Input.Role", "Admin"), ("Input.Username", "admin-test"), ("Input.Password", StaffApplication.Password))).StatusCode);

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string url, string formPage,
        params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestDb _database;
        public HoponDbContext Db => _database.Context;
        public Trip Trip { get; } = new() { Bus = new Bus { RegistrationNumber = "LIFECYCLE" },
            BusRoute = new BusRoute { Name = "Lifecycle route" }, ActualDeparture = Departure };
        public HubSpy Hub { get; } = new();
        public FakeSmsSender Sender { get; } = new();
        public TripLifecycleService Service { get; }

        private Fixture(TestDb database, bool cancellationSms)
        {
            _database = database;
            var notifications = new NotificationService(Db,
                new PassengerNotificationWriter(Db, NullLogger<PassengerNotificationWriter>.Instance),
                TimeProvider.System, Sender, Options.Create(new NotificationOptions {
                    SmsEnabled = true, SmsTypes = cancellationSms ? ["Cancellation"] : [] }));
            Service = new TripLifecycleService(Db, NullLogger<TripLifecycleService>.Instance, Hub, notifications, new LocationThrottle(TimeProvider.System));
        }

        public static async Task<Fixture> CreateAsync(TripStatus status, bool cancellationSms = false)
        {
            var fixture = new Fixture(await TestDb.CreateAsync(), cancellationSms);
            fixture.Trip.Status = status;
            fixture.Db.Trips.Add(fixture.Trip);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.Tickets.AddRange(Enumerable.Range(1, 3).Select(id => new Ticket {
                TripId = fixture.Trip.Id, UserId = id, TicketReference = "LIFECYCLE-" + id, IsActive = id != 3 }));
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed class HubSpy : IHubContext<TripHub>
    {
        public record Broadcast(string Group, string Method, TripStatusBroadcastDto Payload);
        public List<Broadcast> Calls { get; } = new();
        public IHubClients Clients => new SpyClients(this);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class Proxy(HubSpy hub, string group) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                hub.Calls.Add(new(group, method, Assert.IsType<TripStatusBroadcastDto>(Assert.Single(args))));
                return Task.CompletedTask;
            }
        }

        private sealed class SpyClients(HubSpy hub) : IHubClients
        {
            public IClientProxy Group(string groupName) => new Proxy(hub, groupName);
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }
    }
}
