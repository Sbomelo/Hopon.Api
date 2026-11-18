using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminTicketTests
{
    [Theory]
    [InlineData(TripStatus.Scheduled, true)]
    [InlineData(TripStatus.InProgress, false)]
    [InlineData(TripStatus.Delayed, false)]
    [InlineData(TripStatus.Completed, false)]
    [InlineData(TripStatus.Cancelled, false)]
    public async Task Only_Scheduled_trips_allow_void_and_reinstate(TripStatus status, bool allowed)
    {
        using var app = new StaffApplication();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var trip = (await db.Trips.FindAsync(101))!;
        trip.Status = status;
        var ticket = new Ticket { TripId = 101, UserId = 1, TicketReference = "VOID-RULE" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAdminTicketService>();
        Assert.Equal(allowed, (await service.SetActiveAsync(101, ticket.Id, false)).Success);
        Assert.Equal(!allowed, ticket.IsActive);
        ticket.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(allowed, (await service.SetActiveAsync(101, ticket.Id, true)).Success);
        Assert.Equal(allowed, ticket.IsActive);
        Assert.False((await service.SetActiveAsync(102, ticket.Id, false)).Success);
        Assert.False((await service.SetActiveAsync(101, int.MaxValue, false)).Success);
    }

    [Fact]
    public async Task Reinstatement_checks_capacity_seat_and_passenger_and_preserves_reference()
    {
        using var app = new StaffApplication();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var trip = await db.Trips.Include(t => t.Bus).SingleAsync(t => t.Id == 101);
        trip.Bus.Capacity = 1;
        var ticket = new Ticket { TripId = 101, UserId = 1, TicketReference = "KEEP-REF", SeatNumber = "1A", IsActive = false };
        var destination = new TripStop { Trip = trip, SequenceOrder = 2, Stop = new Stop { Name = "Final ticket stop" } };
        db.TripStops.Add(destination);
        var other = new Ticket { TripId = 101, UserId = 2, TicketReference = "OTHER", SeatNumber = "1A" };
        db.Tickets.AddRange(ticket, other);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAdminTicketService>();
        Assert.Contains("full", (await service.SetActiveAsync(101, ticket.Id, true)).Error);
        trip.Bus.Capacity = 2;
        await db.SaveChangesAsync();
        Assert.Contains("already taken", (await service.SetActiveAsync(101, ticket.Id, true)).Error);
        other.IsActive = false;
        var user = (await db.Users.FindAsync(1))!;
        user.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Contains("inactive", (await service.SetActiveAsync(101, ticket.Id, true)).Error);
        user.IsActive = true;
        await db.SaveChangesAsync();
        Assert.True((await service.SetActiveAsync(101, ticket.Id, true)).Success);
        Assert.True((await service.SetActiveAsync(101, ticket.Id, true)).Success);
        Assert.Equal("KEEP-REF", ticket.TicketReference);
        Assert.True((await service.SetActiveAsync(101, ticket.Id, false)).Success);
        var issued = await scope.ServiceProvider.GetRequiredService<IAdminTripService>().IssueTicketAsync(101, 1, "1A", destination.Id);
        Assert.False(issued.Success);
        Assert.Contains("already has a ticket", issued.Error);
        Assert.Equal(2, await db.Tickets.CountAsync(t => t.TripId == 101));
        Assert.False((await new TripAccessService(db).CheckTripAccessAsync(1, 101)).HasAccess);
        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyTripDelayedAsync(trip, "test");
        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyTripCancelledAsync(trip, "test");
        Assert.Empty(await db.NotificationLogs.ToListAsync());
    }

    [Fact]
    public async Task Void_form_keeps_issue_validation_separate_and_revokes_existing_tracking_sessions()
    {
        using var app = new StaffApplication();
        using var admin = app.NewClient();
        using var passenger = app.NewClient();
        int ticketId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            var ticket = new Ticket { TripId = 101, UserId = 1, TicketReference = "VOID-HTTP" };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            ticketId = ticket.Id;
            passenger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Users.FindAsync(1))!).Token);
        }
        await Post(admin, "/Staff/Login", "/Staff/Login", ("Input.Role", "Admin"), ("Input.Username", "admin-test"), ("Input.Password", StaffApplication.Password));
        var sessions = app.Services.GetRequiredService<TripTrackingSessions>();
        await sessions.JoinAsync(1, 101, "open-map", () => Task.CompletedTask);
        const string path = "/Admin/Trips/Details/101";
        Assert.Contains(">Void</button>", await admin.GetStringAsync(path));
        var voidResponse = await Post(admin, path + "?handler=Void", path, ("TicketAction.TicketId", ticketId.ToString()));
        Assert.True(voidResponse.StatusCode == HttpStatusCode.Redirect, await voidResponse.Content.ReadAsStringAsync());
        Assert.Contains(">Reinstate</button>", await admin.GetStringAsync(path));
        Assert.Equal(HttpStatusCode.NotFound, (await passenger.GetAsync("/api/trips/101/map-data")).StatusCode);
        var leftover = new List<string>();
        await sessions.RevokeAsync(1, 101, connection => { leftover.Add(connection); return Task.CompletedTask; });
        Assert.Empty(leftover);
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, path + "?handler=Reinstate", path, ("TicketAction.TicketId", "invalid"))).StatusCode);
        Assert.Contains(">Reinstate</button>", await admin.GetStringAsync(path));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, path + "?handler=Reinstate", path, ("TicketAction.TicketId", ticketId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await passenger.GetAsync("/api/trips/101/map-data")).StatusCode);
    }

    [Fact]
    public async Task Tracking_revocation_waits_for_an_in_flight_join_and_cleans_up_disconnections()
    {
        var sessions = new TripTrackingSessions();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var join = sessions.JoinAsync(1, 5, "one", async () => { entered.SetResult(); await release.Task; });
        await entered.Task;
        var removed = new List<string>();
        var revoke = sessions.RevokeAsync(1, 5, connection => { removed.Add(connection); return Task.CompletedTask; });
        Assert.False(revoke.IsCompleted);
        release.SetResult();
        await Task.WhenAll(join, revoke);
        Assert.Equal(new[] { "one" }, removed);
        await sessions.JoinAsync(2, 5, "two", () => Task.CompletedTask);
        await sessions.LeaveAsync("two");
        await sessions.RevokeAsync(2, 5, _ => throw new Exception("Disconnected connection was retained"));
    }

    [Fact]
    public async Task Void_removes_an_open_websocket_from_the_trip_group_and_blocks_rejoining()
    {
        using var app = new StaffApplication();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
        var ticket = new Ticket { TripId = 101, UserId = 1, TicketReference = "VOID-SOCKET" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken((await db.Users.FindAsync(1))!).Token;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var socket = await app.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/hubs/trip?access_token=" + token), timeout.Token);
        await Send(socket, "{\"protocol\":\"json\",\"version\":1}", timeout.Token);
        await Receive(socket, timeout.Token);
        await Send(socket, "{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinTrip\",\"arguments\":[101]}", timeout.Token);
        Assert.False((await Receive(socket, timeout.Token)).TryGetProperty("error", out _));
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<TripHub>>();
        await hub.Clients.Group(TripHub.GroupName(101)).SendAsync("BeforeVoidProbe", timeout.Token);
        Assert.Equal("BeforeVoidProbe", (await Receive(socket, timeout.Token)).GetProperty("target").GetString());
        Assert.True((await scope.ServiceProvider.GetRequiredService<IAdminTicketService>().SetActiveAsync(101, ticket.Id, false)).Success);
        await hub.Clients.Group(TripHub.GroupName(101)).SendAsync("ForbiddenProbe", timeout.Token);
        await hub.Clients.All.SendAsync("AfterVoidProbe", timeout.Token);
        Assert.Equal("AfterVoidProbe", (await Receive(socket, timeout.Token)).GetProperty("target").GetString());
        await Send(socket, "{\"type\":1,\"invocationId\":\"2\",\"target\":\"JoinTrip\",\"arguments\":[101]}", timeout.Token);
        Assert.Contains("do not have access", (await Receive(socket, timeout.Token)).GetProperty("error").GetString());
    }

    private static Task Send(WebSocket socket, string json, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json + "\u001e")), WebSocketMessageType.Text, true, token);

    private static async Task<JsonElement> Receive(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count).TrimEnd('\u001e'));
        return json.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, string formPage, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }
}
