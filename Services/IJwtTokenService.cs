using Hopon.Api.Models; 

namespace Hopon.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);

}