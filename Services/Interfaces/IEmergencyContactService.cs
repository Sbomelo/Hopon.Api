using Hopon.Api.DTOs.EmergencyContact;

namespace Hopon.Api.Services.Interfaces
{
    public interface IEmergencyContactService
    {
        Task<EmergencyContactDto?> GetByUserIdAsync(int userId);
        Task<EmergencyContactDto> UpsertAsync(int userId, UpsertEmergencyContactDto dto);
        Task<bool> DeleteByUserIdAsync(int userId);
    }
}
