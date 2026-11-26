using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services.Notifications;
using Hopon.Api.Services.Sms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var resetDemo = args.Contains("--reset-demo");
var seedDemo = resetDemo || args.Contains("--seed-demo");
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--seed-demo" && a != "--reset-demo").ToArray());

builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt")
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<Hopon.Api.Services.CorsOptions>().BindConfiguration("Cors")
    .Validate(options => options.HasValidOrigins(),
        "Cors:AllowedOrigins must contain only HTTP/HTTPS origins without paths, trailing slashes or wildcards.")
    .ValidateOnStart();
builder.Services.AddOptions<OtpOptions>().BindConfiguration("Otp").ValidateOnStart();
builder.Services.AddSmsSending();
builder.Services.AddOptions<NotificationOptions>().Configure<IConfiguration>((options, configuration) =>
    {
        options.SmsEnabled = configuration.GetValue<bool>("Notifications:SmsEnabled");
        options.SmsTypes = configuration.GetSection("Notifications:SmsTypes").Get<string[]>() ?? options.SmsTypes;
    })
    .Validate(o => o.HasValidTypes(), "Notifications:SmsTypes must contain NotificationType names.").ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

//Swagger UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Hopon.Api",
        Version = "v1",
        Description = "API for testing purposes"
    });
});

//REGISTER SERVICES
builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AuthorizeFolder("/Driver", "DriverOnly");
    options.Conventions.AuthorizeFolder("/Staff", "StaffOnly");
    options.Conventions.AllowAnonymousToPage("/Staff/Login");
    options.Conventions.AllowAnonymousToPage("/Staff/Denied");
});
builder.Services.AddScoped<StaffSeeder>();
builder.Services.AddScoped<DemoSeeder>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<SimulatorService>();
builder.Services.AddScoped<AdminDriverService>();
builder.Services.AddScoped<StaffCookieEvents>();
builder.Services.AddSignalR();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddDbContext<HoponDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HoponDb")));

builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<ITripAccessService, TripAccessService>();
builder.Services.AddScoped<ITripDashboardService, TripDashboardService>();
builder.Services.AddScoped<IAdminTripService, AdminTripService>();
builder.Services.AddScoped<IAdminConsoleService, AdminConsoleService>();
builder.Services.AddScoped<IAdminTicketService, AdminTicketService>();
builder.Services.AddScoped<IAdminTripActionsService, AdminTripActionsService>();
builder.Services.AddSingleton<TripTrackingSessions>();
builder.Services.AddScoped<IAdminMasterDataService, AdminMasterDataService>();
builder.Services.AddScoped<IDriverAuthService, DriverAuthService>();
builder.Services.AddScoped<IDriverTripAccessService, DriverTripAccessService>();
builder.Services.AddScoped<ITripLifecycleService, TripLifecycleService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILocationThrottle, LocationThrottle>();
builder.Services.AddSingleton<AccessAuditService>();
builder.Services.AddOptions<RetentionOptions>().BindConfiguration("Retention")
    .Validate(o => o.DaysAfterTrip > 0 && o.LocationDays > 0, "Retention day limits must be positive.").ValidateOnStart();
builder.Services.AddSingleton<RetentionRunner>();
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddScoped<PassengerNotificationWriter>();
builder.Services.AddHostedService<NotificationSchedulerService>();
builder.Services.AddScoped<IBoardingService, BoardingService>();
builder.Services.AddScoped<ITripHistoryService, TripHistoryService>();
builder.Services.AddScoped<ITripMapService, TripMapService>();
builder.Services.AddScoped<IEmergencyContactService, EmergencyContactService>();


//JWT CONFIGURATION
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
.Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
{
    var jwt = jwtOptions.Value;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        
        ValidateAudience = true,
        ValidAudience = jwt.Audience,

        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = ClaimTypes.Role,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var passenger = context.Principal!;
            if (!passenger.IsInRole("Passenger")) return;

            var phoneNumber = passenger.FindFirstValue("phoneNumber");
            var db = context.HttpContext.RequestServices.GetRequiredService<HoponDbContext>();
            if (!int.TryParse(passenger.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
                !await db.Users.AnyAsync(user => user.Id == userId && user.IsActive && user.PhoneNumber == phoneNumber,
                    context.HttpContext.RequestAborted))
            {
                context.Fail("The passenger session is no longer current.");
            }
        }
    };
});

builder.Services.AddAuthentication().AddCookie(StaffCookieEvents.Scheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Staff/Login";
    options.AccessDeniedPath = "/Staff/Denied";
    options.EventsType = typeof(StaffCookieEvents);
});

// Razor policies explicitly use cookies; APIs retain the JWT defaults above.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StaffOnly", policy => policy
        .AddAuthenticationSchemes(StaffCookieEvents.Scheme).RequireAuthenticatedUser());
    options.AddPolicy("AdminOnly", policy => policy
        .AddAuthenticationSchemes(StaffCookieEvents.Scheme).RequireRole("Admin"));
    options.AddPolicy("DriverOnly", policy => policy
        .AddAuthenticationSchemes(StaffCookieEvents.Scheme).RequireRole("Driver"));
});

builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
.Configure<IOptions<Hopon.Api.Services.CorsOptions>>((options, corsOptions) =>
{
    options.AddPolicy("AllowPassengerApp",
        policy =>
        {
            policy.WithOrigins(corsOptions.Value.AllowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
        });
});



var app = builder.Build();

// Validate before the staff bootstrap touches the database.
app.Services.GetRequiredService<IStartupValidator>().Validate();

using (var scope = app.Services.CreateScope())
{
    if (seedDemo || (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Demo:SeedOnStartup")))
    {
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(resetDemo);
        app.Logger.LogInformation("Hopon demo data is ready.");
        if (seedDemo) return;
    }
    await scope.ServiceProvider.GetRequiredService<StaffSeeder>().SeedAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Hopon.Api v1");
    });
}

app.UseStaticFiles();

app.UseCors("AllowPassengerApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHub<TripHub>("/hubs/trip");

app.Run();

public partial class Program { }
