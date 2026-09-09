using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.DTOs.EmergencyContact
{
    public class UpsertEmergencyContactDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = null!;
    }
}
