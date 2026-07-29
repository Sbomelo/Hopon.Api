using Hopon.Api.DTOs.Drivers;
using Hopon.Api.Services;
using Hopon.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Hopon.Api.Controllers
{
    [ApiController]
    [Route("api/driver/auth")]
    public class DriverAuthController : ControllerBase
    {
        private readonly IDriverAuthService _driverAuthService;
        private readonly IJwtTokenService _jwtTokenService;

        public DriverAuthController(IDriverAuthService driverAuthService, IJwtTokenService jwtTokenService)
        {
            _driverAuthService = driverAuthService;
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("login")]
        public async Task<ActionResult<DriverAuthController>> Login([FromBody] DriverLoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Username and password are required");

            var (success, error, driver) = await _driverAuthService.LoginAsync(dto.Username, dto.Password);

            if (!success || driver is null)
                return Unauthorized(error);

            var (token, expiresAt) = _jwtTokenService.GenerateToken(driver);

            return Ok(new DriverAuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                DriverId = driver.Id,
                FullName = driver.FullName
            });
        }
    }
}
