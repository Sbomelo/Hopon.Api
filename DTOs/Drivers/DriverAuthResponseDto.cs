namespace Hopon.Api.DTOs.Drivers
{
    public class DriverAuthResponseDto
    {
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public int DriverId { get; set; }
        public string FullName { get; set; } = null!;

    }
}
