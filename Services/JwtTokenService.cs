using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hopon.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hopon.Api.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        //Build the signing Key
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        //Set expiry
        var expiresAt = DateTime.UtcNow.AddHours(_options.ExpiryHours);

        //Build Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, "Passenger"),
            new("phoneNumber", user.PhoneNumber),
            new(ClaimTypes.Name, user.FullName)
        };

        //Create Token
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

     return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(Driver driver)
    {
        //Build the signing Key
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        //Set expiry
        var expiresAt = DateTime.UtcNow.AddHours(_options.ExpiryHours);

        //Build Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, driver.Id.ToString()),
            new(ClaimTypes.Name, driver.FullName),
            new(ClaimTypes.Role, "Driver"),
            new("username", driver.Username)
        };

        //Create Token
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }


}
