using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.Services;

public class JwtOptions
{
    [Required(ErrorMessage = "Jwt:Key is required. Set it with user-secrets or the Jwt__Key environment variable.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must contain at least 32 characters.")]
    public string Key { get; set; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Issuer is required.")]
    public string Issuer { get; set; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience is required.")]
    public string Audience { get; set; } = string.Empty;

    [Range(0.01, 8760, ErrorMessage = "Jwt:ExpiryHours must be between 0.01 and 8760.")]
    public double ExpiryHours { get; set; } = 8;
}
