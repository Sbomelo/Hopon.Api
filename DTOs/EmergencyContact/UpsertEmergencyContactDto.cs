using System.ComponentModel.DataAnnotations;

using Hopon.Api.Services;

namespace Hopon.Api.DTOs.EmergencyContact
{
    public class UpsertEmergencyContactDto : IValidatableObject
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = null!;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!PhoneNumberNormalizer.TryNormalizeSouthAfrican(PhoneNumber, out _))
                yield return new ValidationResult(
                    "A valid South African mobile phone number is required.",
                    new[] { nameof(PhoneNumber) });
        }
    }
}
