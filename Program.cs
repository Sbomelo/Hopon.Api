using Hopon.Api.Data;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

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
builder.Services.AddRazorPages();

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddDbContext<HoponDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HoponDb")));

builder.Services.AddScoped<ITripAccessService, TripAccessService>();
builder.Services.AddScoped<ITripDashboardService, TripDashboardService>();
builder.Services.AddScoped<IAdminTripService, AdminTripService>();


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
        IssuerSigningKey = new SymmetricSecurityKey( Encoding.UTF8.GetBytes(jwtSection["Key"]!))
    };
});

// Authorization
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Hopon.Api v1");
    });
}

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();