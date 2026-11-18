using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hopon.Api.Data;
using Hopon.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hopon.Api.Tests;

public class AdminConsoleHttpTests
{
    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Monitor")]
    [InlineData("/Admin/Monitor?handler=Trips")]
    [InlineData("/Admin/Notifications")]
    [InlineData("/Admin/Buses")]
    [InlineData("/Admin/Buses/Edit")]
    [InlineData("/Admin/Stops")]
    [InlineData("/Admin/Stops/Edit")]
    [InlineData("/Admin/Routes")]
    [InlineData("/Admin/Routes/Edit")]
    public async Task Every_new_page_and_monitor_handler_require_an_admin_cookie(string path)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        Assert.Contains("/Staff/Login", (await client.GetAsync(path)).Headers.Location!.OriginalString);
        await Login(client, "Driver", "driver-one");
        Assert.Contains("/Staff/Denied", (await client.GetAsync(path)).Headers.Location!.OriginalString);
        await Login(client, "Admin", "admin-test");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Master_data_forms_validate_and_order_stops_with_independent_POST_handlers()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        await Login(client, "Admin", "admin-test");
        const string busPage = "/Admin/Buses/Edit";
        var invalid = await Post(client, busPage, busPage, ("Input.RegistrationNumber", "NEW-123"), ("Input.Capacity", "0"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("validation-summary-errors", await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, busPage, busPage,
            ("Input.RegistrationNumber", "NEW-123"), ("Input.Model", "Demo coach"), ("Input.Capacity", "20"))).StatusCode);
        const string stopPage = "/Admin/Stops/Edit";
        invalid = await Post(client, stopPage, stopPage, ("Input.Name", "Bad"), ("Input.Latitude", "91"), ("Input.Longitude", "0"));
        Assert.Contains("validation-summary-errors", await invalid.Content.ReadAsStringAsync());
        foreach (var (name, longitude) in new[] { ("HTTP A", "30"), ("HTTP B", "31") })
            Assert.Equal(HttpStatusCode.Redirect, (await Post(client, stopPage, stopPage,
                ("Input.Name", name), ("Input.Latitude", "-29"), ("Input.Longitude", longitude))).StatusCode);
        const string routePage = "/Admin/Routes/Edit";
        var created = await Post(client, routePage, routePage, ("Input.Name", "HTTP route"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var path = created.Headers.Location!.OriginalString;
        int[] ids;
        using (var scope = app.Services.CreateScope()) ids = await scope.ServiceProvider.GetRequiredService<HoponDbContext>()
            .Stops.Where(s => s.Name.StartsWith("HTTP ")).OrderBy(s => s.Name).Select(s => s.Id).ToArrayAsync();
        foreach (var id in ids)
            Assert.Equal(HttpStatusCode.Redirect, (await Post(client, path + "?handler=Add", path, ("Addition.StopId", id.ToString()))).StatusCode);
        int routeStopId;
        using (var scope = app.Services.CreateScope()) routeStopId = await scope.ServiceProvider.GetRequiredService<HoponDbContext>()
            .RouteStops.Where(s => s.StopId == ids[1]).Select(s => s.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, path + "?handler=MoveUp", path, ("Change.RouteStopId", routeStopId.ToString()))).StatusCode);
        var html = await client.GetStringAsync(path);
        Assert.True(html.IndexOf("<td>HTTP B</td>", StringComparison.Ordinal) < html.IndexOf("<td>HTTP A</td>", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, path + "?handler=Remove", path, ("Change.RouteStopId", routeStopId.ToString()))).StatusCode);
        Assert.DoesNotContain("<td>HTTP B</td>", await client.GetStringAsync(path));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path + "?handler=Remove", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Contains("openstreetmap.org", await client.GetStringAsync("/Admin/Stops"));
    }

    [Fact]
    public async Task Monitor_returns_private_JSON_and_log_rejects_invalid_filters_without_mutation()
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        await Login(client, "Admin", "admin-test");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoponDbContext>();
            (await db.Trips.FindAsync(101))!.Status = TripStatus.InProgress;
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/Admin/Monitor?handler=Trips");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(rows.RootElement.EnumerateArray());
        Assert.Equal(101, rows.RootElement[0].GetProperty("tripId").GetInt32());
        Assert.Contains("monitor.js", await client.GetStringAsync("/Admin/Monitor"));
        foreach (var query in new[] { "Filter.PageNumber=0", "Filter.Type=999", "Filter.TripId=invalid" })
            Assert.Contains("validation-summary-errors", await client.GetStringAsync("/Admin/Notifications?" + query));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Admin/Notifications", new FormUrlEncodedContent([]))).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string role, string username) => Post(client,
        "/Staff/Login", "/Staff/Login", ("Input.Role", role), ("Input.Username", username), ("Input.Password", StaffApplication.Password));

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, string formPage, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPage);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }
}
