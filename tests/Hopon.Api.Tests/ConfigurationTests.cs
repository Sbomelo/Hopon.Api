using System.Net;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hopon.Api.Tests;

public class ConfigurationTests
{
    [Theory]
    [InlineData("Jwt:Key", "", "Jwt:Key is required")]
    [InlineData("Jwt:Key", "too-short", "at least 32 characters")]
    [InlineData("Jwt:Issuer", "", "Jwt:Issuer is required")]
    [InlineData("Jwt:Audience", "", "Jwt:Audience is required")]
    [InlineData("Jwt:ExpiryHours", "0", "Jwt:ExpiryHours")]
    [InlineData("Cors:AllowedOrigins:0", "*", "Cors:AllowedOrigins")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.com/path", "Cors:AllowedOrigins")]
    public void Invalid_configuration_fails_at_startup(string key, string value, string expected)
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

        var error = Assert.Throws<OptionsValidationException>(() => app.CreateClient());

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task Development_preflight_allows_only_configured_origin(string origin, bool allowed)
    {
        using var app = new StaffApplication();
        using var client = app.NewClient();
        using var request = Preflight(origin);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    [Fact]
    public async Task Production_has_no_default_origins_disables_swagger_and_enforces_https_and_hsts()
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://hopon.example")
        });
        Assert.Empty(app.Services.GetRequiredService<IOptions<Hopon.Api.Services.CorsOptions>>().Value.AllowedOrigins);
        Assert.False(app.Services.GetRequiredService<IOptions<OtpOptions>>().Value.ExposeCodeInResponse);
        using var preflight = Preflight("http://localhost:5173");
        var cors = await client.SendAsync(preflight);
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Origin"));

        var swagger = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        Assert.True(swagger.Headers.Contains("Strict-Transport-Security"));
        var http = await client.GetAsync("http://hopon.example/api/trips/my-trips");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, http.StatusCode);
        Assert.Equal("https", http.Headers.Location?.Scheme);
    }

    [Fact]
    public async Task Trusted_proxy_https_header_is_processed_before_redirection()
    {
        using var baseApp = new StaffApplication();
        using var app = baseApp.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services => services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
                options.KnownProxies.Add(IPAddress.Parse("192.0.2.10"))));
        });
        // Set the connection peer explicitly to exercise the middleware's proxy trust check.
        var context = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
            context.Request.Scheme = "http";
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString("hopon.example");
            context.Request.Path = "/api/trips/my-trips";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "192.0.2.20";
        });
        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("192.0.2.20"), context.Connection.RemoteIpAddress);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.True(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/otp/request");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }
}
