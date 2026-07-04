using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hopon.Api.Models;
//using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;

namespace Hopon.Api.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        //Build the signing Key
        var jwtSection = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        //Set expiry
        var expiresAt = DateTime.UtcNow.AddHours(double.Parse(jwtSection["ExpiryHours"]!));

        //Build Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("phoneNumber", user.PhoneNumber),
            new(ClaimTypes.Name, user.FullName)
        };

        //Create Token
        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

     return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}