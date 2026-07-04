using Hopon.Api.Data;
using Hopon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;




var builder = WebApplication.CreateBuilder(args);

//REGISTER SERVICES
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddDbContext<HoponDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HoponDb")));


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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.Run();