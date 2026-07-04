namespace Hopon.Api.Models;

public class OtpRequest
{
    public int Id {get; set;}
    public string PhoneNumber {get; set; } = null!;
    public string OtpCodeHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public int AttemptCount { get; set; }
}