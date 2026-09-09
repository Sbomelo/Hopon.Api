using Hopon.Api.Data;
using Hopon.Api.DTOs.EmergencyContact;
using Hopon.Api.Models;
using Hopon.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Services
{
    public class EmergencyContactService : IEmergencyContactService
    {
        private readonly HoponDbContext _db;

        public EmergencyContactService(HoponDbContext db)
        {
            _db = db;
        }

        public async Task<EmergencyContactDto?> GetByUserIdAsync(int userId)
        {
            var contact = await _db.EmergencyContacts
                .AsNoTracking()
                .FirstOrDefaultAsync(ec => ec.UserId == userId);

            return contact is null ? null : ToDto(contact);

        }

        public async Task<EmergencyContactDto> UpsertAsync(int userId, UpsertEmergencyContactDto dto)
        {
            var existing = await _db.EmergencyContacts
                .FirstOrDefaultAsync(ec => ec.UserId == userId);

            if (existing is null)
            {
                var contact = new EmergencyContact
                {
                    UserId = userId,
                    Name = dto.Name.Trim(),
                    PhoneNumber = dto.PhoneNumber.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _db.EmergencyContacts.Add(contact);
                await _db.SaveChangesAsync();

                return ToDto(contact);
            }

            existing.Name = dto.Name.Trim();
            existing.PhoneNumber = dto.PhoneNumber.Trim();
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return ToDto(existing);
        }
        public async Task<bool> DeleteByUserIdAsync(int userId)
        {
            var contact = await _db.EmergencyContacts
                .FirstOrDefaultAsync(ec => ec.UserId == userId);

            if (contact is null)
                return false;

            _db.EmergencyContacts.Remove(contact);
            await _db.SaveChangesAsync();

            return true;
        }

        private static EmergencyContactDto ToDto(EmergencyContact c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }

        
}
