using Hopon.Api.Data;
using Hopon.Api.Hubs;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<AdminDriverService>();
builder.Services.AddScoped<StaffCookieEvents>();
builder.Services.AddSignalR();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddDbContext<HoponDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HoponDb")));

builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ITripAccessService, TripAccessService>();
builder.Services.AddScoped<ITripDashboardService, TripDashboardService>();
builder.Services.AddScoped<IAdminTripService, AdminTripService>();
builder.Services.AddScoped<IDriverAuthService, DriverAuthService>();
builder.Services.AddScoped<IDriverTripAccessService, DriverTripAccessService>();
builder.Services.AddScoped<IDriverTripService, DriverTripService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IBoardingService, BoardingService>();
builder.Services.AddScoped<ITripHistoryService, TripHistoryService>();
builder.Services.AddScoped<ITripMapService, TripMapService>();
builder.Services.AddScoped<IEmergencyContactService, EmergencyContactService>();


//JWT CONFIGURATION
var jwtSection = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSection["Issuer"],
        
        ValidateAudience = true,
        ValidAudience = jwtSection["Audience"],

        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = ClaimTypes.Role,
        IssuerSigningKey = new SymmetricSecurityKey( Encoding.UTF8.GetBytes(jwtSection["Key"]!))
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowPassengerApp",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
        });
});



var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<StaffSeeder>().SeedAsync();
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
