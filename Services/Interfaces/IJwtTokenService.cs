using Hopon.Api.Models; 

namespace Hopon.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
    (string Token, DateTime ExpiresAt) GenerateToken(Driver driver);

}