namespace Hopon.Api.DTOs.Auth;

public class AuthResponseDto
{
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int UserId { get; set; }
    public string FullName  {get; set;} = null!;
    public string PhoneNumber { get; set; } = null!;
}