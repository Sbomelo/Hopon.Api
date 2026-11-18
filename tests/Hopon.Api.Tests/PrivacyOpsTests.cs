using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hopon.Api.Tests;

public class PrivacyOpsTests
{
    [Theory]
    [InlineData(2.999, LocationOutcome.Throttled)]
    [InlineData(3, LocationOutcome.Recorded)]
    [InlineData(3.001, LocationOutcome.Recorded)]
    public async Task Throttle_boundary_is_three_seconds(double seconds, LocationOutcome expected)
    {
        var clock = new FakeTimeProvider();
        var throttle = new LocationThrottle(clock);
        async Task<LocationRecordResult> Send(int id) => await throttle.RecordAsync(id, -29, 31,
            () => Task.FromResult<LocationUpdate?>(null), _ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.Equal(LocationOutcome.Recorded, (await Send(1)).Outcome);
        clock.Advance(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, (await Send(1)).Outcome);
        Assert.Equal(LocationOutcome.Recorded, (await Send(2)).Outcome);
    }

    [Fact]
    public async Task Broadcasts_every_accepted_fix_and_persists_at_fifteen_seconds()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LocationThrottle(clock);
        var persisted = 0;
        var broadcasts = 0;
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(LocationOutcome.Recorded, (await throttle.RecordAsync(1, -29, 31,
                () => Task.FromResult<LocationUpdate?>(null), _ => { persisted++; return Task.CompletedTask; }, _ => { broadcasts++; return Task.CompletedTask; })).Outcome);
            clock.Advance(TimeSpan.FromSeconds(3));
        }
        Assert.Equal(7, broadcasts);
        Assert.Equal(2, persisted);
    }

    [Fact]
    public async Task Concurrent_fixes_accept_only_one()
    {
        var throttle = new LocationThrottle(new FakeTimeProvider());
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => throttle.RecordAsync(1, 0, 0,
            () => Task.FromResult<LocationUpdate?>(null), _ => Task.CompletedTask, _ => Task.CompletedTask)));
        Assert.Single(results, x => x.Outcome == LocationOutcome.Recorded);
    }

    [Fact]
    public async Task Plausibility_uses_previous_accepted_fix_and_rejected_fix_does_not_advance_clock()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LocationThrottle(clock);
        Task<LocationRecordResult> Send(double lat) => throttle.RecordAsync(1, lat, 0,
            () => Task.FromResult<LocationUpdate?>(null), _ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.Equal(LocationOutcome.Recorded, (await Send(0)).Outcome);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(LocationOutcome.Recorded, (await Send(.001)).Outcome);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(LocationOutcome.Rejected, (await Send(-.001)).Outcome);
        Assert.Equal(LocationOutcome.Recorded, (await Send(.002)).Outcome);
    }

    [Fact]
    public async Task Restart_loads_persisted_fix_for_jump_and_write_interval()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LocationThrottle(clock);
        var previous = new LocationUpdate { Latitude = 0, Longitude = 0, RecordedAt = clock.GetUtcNow().UtcDateTime.AddSeconds(-5) };
        var persisted = false;
        Assert.Equal(LocationOutcome.Rejected, (await throttle.RecordAsync(1, 1, 1,
            () => Task.FromResult<LocationUpdate?>(previous), _ => Task.CompletedTask, _ => Task.CompletedTask)).Outcome);
        Assert.Equal(LocationOutcome.Recorded, (await throttle.RecordAsync(1, 0, 0,
            () => Task.FromResult<LocationUpdate?>(previous), _ => { persisted = true; return Task.CompletedTask; }, _ => Task.CompletedTask)).Outcome);
        Assert.False(persisted);
    }

    [Theory]
    [InlineData(91, 0)] [InlineData(0, -181)] [InlineData(double.NaN, 0)] [InlineData(0, double.PositiveInfinity)]
    public async Task Invalid_coordinates_are_rejected(double lat, double lng)
        => Assert.Equal(LocationOutcome.Rejected, (await new LocationThrottle(new FakeTimeProvider()).RecordAsync(1, lat, lng,
            () => throw new Exception("Should not read"), _ => throw new Exception("Should not persist"), _ => throw new Exception("Should not publish"))).Outcome);

    [Theory]
    [InlineData(249.99, LocationOutcome.Recorded)]
    [InlineData(250.01, LocationOutcome.Rejected)]
    public async Task Speed_limit_boundary(double speed, LocationOutcome expected)
    {
        var clock = new FakeTimeProvider();
        var previous = new LocationUpdate { RecordedAt = clock.GetUtcNow().UtcDateTime.AddHours(-1) };
        var latitude = speed / 6371 * 180 / Math.PI;
        Assert.Equal(expected, (await new LocationThrottle(clock).RecordAsync(1, latitude, 0,
            () => Task.FromResult<LocationUpdate?>(previous), _ => Task.CompletedTask, _ => Task.CompletedTask)).Outcome);
    }

    [Fact]
    public async Task Startup_observes_three_second_boundary_from_last_saved_fix()
    {
        var clock = new FakeTimeProvider();
        var previous = new LocationUpdate { RecordedAt = clock.GetUtcNow().UtcDateTime.AddSeconds(-2) };
        Assert.Equal(LocationOutcome.Throttled, (await new LocationThrottle(clock).RecordAsync(1, 0, 0,
            () => Task.FromResult<LocationUpdate?>(previous), _ => Task.CompletedTask, _ => Task.CompletedTask)).Outcome);
    }

    [Fact]
    public async Task Broadcast_failure_does_not_allow_an_extra_database_write()
    {
        var clock = new FakeTimeProvider();
        var throttle = new LocationThrottle(clock);
        var writes = 0;
        Task Save(LocationUpdate fix) { writes++; return Task.CompletedTask; }
        await Assert.ThrowsAsync<InvalidOperationException>(() => throttle.RecordAsync(1, 0, 0,
            () => Task.FromResult<LocationUpdate?>(null), Save, _ => throw new InvalidOperationException()));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(LocationOutcome.Recorded, (await throttle.RecordAsync(1, 0, 0,
            () => Task.FromResult<LocationUpdate?>(null), Save, _ => Task.CompletedTask)).Outcome);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Audit_rate_limit_is_per_user_trip_action_and_allows_exact_five_minutes()
    {
        await using var f = await Fixture.Create();
        var audit = new AccessAuditService(f.Provider.GetRequiredService<IServiceScopeFactory>(), f.Clock, NullLogger<AccessAuditService>.Instance);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => audit.RecordAsync(1, 1, AccessAction.ViewedMap)));
        await audit.RecordAsync(2, 1, AccessAction.ViewedMap);
        await audit.RecordAsync(1, 2, AccessAction.ViewedMap);
        await audit.RecordAsync(1, 1, AccessAction.ViewedHistory);
        await audit.RecordAsync(1, null, AccessAction.ViewedNotifications);
        Assert.Equal(5, await f.Db.AccessAuditLogs.CountAsync());
        f.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1));
        await audit.RecordAsync(1, 1, AccessAction.ViewedMap);
        Assert.Equal(5, await f.Db.AccessAuditLogs.CountAsync());
        f.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await audit.RecordAsync(1, 1, AccessAction.ViewedMap);
        Assert.Equal(6, await f.Db.AccessAuditLogs.CountAsync());
    }

    [Fact]
    public async Task Audit_database_failure_is_swallowed()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        await new AccessAuditService(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<AccessAuditService>.Instance).RecordAsync(1, null, AccessAction.ViewedNotifications);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ServiceProvider Provider { get; private set; } = null!;
        public HoponDbContext Db { get; private set; } = null!;
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f._connection.OpenAsync();
            f.Provider = new ServiceCollection().AddDbContext<HoponDbContext>(o => o.UseSqlite(f._connection)).BuildServiceProvider();
            f.Db = f.Provider.GetRequiredService<HoponDbContext>();
            await f.Db.Database.EnsureCreatedAsync();
            return f;
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
