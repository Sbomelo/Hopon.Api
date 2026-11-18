using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class PrivacyOpsHttpTests
{
    [Fact]
    public async Task Driver_api_returns_429_and_400_and_does_not_persist_bad_fixes()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Drivers.FindAsync(101))!).Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/driver/trips/101/location", new { latitude = -29, longitude = 31 })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/driver/trips/101/location", new { latitude = -29, longitude = 31 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/driver/trips/101/location", new { latitude = 100, longitude = 31 })).StatusCode);
        Assert.Single(await db.LocationUpdates.ToListAsync());
    }

    [Fact]
    public async Task Passenger_reads_are_audited_only_after_access_is_granted()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        db.Tickets.Add(new Ticket { UserId = 1, TripId = 101, TicketReference = "AUDIT" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Users.FindAsync(1))!).Token);
        foreach (var endpoint in new[] { "/api/trips/101/map-data", "/api/trips/101/history", "/api/notifications/my" })
            for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trips/102/map-data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trips/102/history")).StatusCode);
        var logs = await db.AccessAuditLogs.ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, x => Assert.Equal(1, x.UserId));
        Assert.DoesNotContain(logs, x => x.TripId == 102);
        Assert.Contains(logs, x => x.Action == AccessAction.ViewedNotifications && x.TripId == null);
    }

}
