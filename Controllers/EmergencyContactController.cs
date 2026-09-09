using Hopon.Api.DTOs.EmergencyContact;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hopon.Api.Controllers
{
    [Route("api/emergency-contact")]
    [ApiController]
    [Authorize]
    public class EmergencyContactController : ControllerBase
    {
        private readonly IEmergencyContactService _service;

        public EmergencyContactController(IEmergencyContactService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var userId = GetUserId();
            if (userId is null) return Unauthorized();

            var contact = await _service.GetByUserIdAsync(userId.Value);
            return Ok(contact);
        }

        [HttpPut]
        public async Task<IActionResult> Upsert([FromBody] UpsertEmergencyContactDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = GetUserId();
            if (userId is null) return Unauthorized();

            var result = await _service.UpsertAsync(userId.Value, dto);
            return Ok(result);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete()
        {
            var userId = GetUserId();
            if (userId is null) return Unauthorized();

            var deleted = await _service.DeleteByUserIdAsync(userId.Value);

            if (!deleted)
                return NotFound(new { message = "No emergency contact found to delete." });

            return NoContent();
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return claim is not null && int.TryParse(claim, out var id) ? id : null;
        }
    }
}
