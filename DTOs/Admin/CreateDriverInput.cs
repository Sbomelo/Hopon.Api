using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.DTOs.Admin;

public class CreateDriverInput
{
    [Required, StringLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, Phone, StringLength(30)] public string PhoneNumber { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Username { get; set; } = string.Empty;
    [Required, MinLength(8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
}
