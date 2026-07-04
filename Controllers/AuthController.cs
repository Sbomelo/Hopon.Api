using Hopon.Api.Data;
using Hopon.Api.DTOs.Auth;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Controllers;


[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IOtpService _otpService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly HoponDbContext _db;

    public AuthController(IOtpService otpService, IJwtTokenService jwtTokenService, HoponDbContext db)
    {
        _jwtTokenService = jwtTokenService;
        _otpService = otpService;
        _db = db;
    }

    [HttpPost("otp/request")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpDto dto)
    {
        if(string.IsNullOrWhiteSpace(dto.PhoneNumber))
        {
            return BadRequest("Phone number is required.");
        }
                
        var (success, error, devOnlyCode) = await _otpService.RequestOtpAsync(dto.PhoneNumber);

        if(!success)
            return BadRequest(error);

        return Ok(new {message = "OTP sent", devOnlyCode});
    }

    [HttpPost("otp/verify")]
    public async Task<ActionResult<AuthResponseDto>> VerifyOtp ([FromBody] VerifyOtpDto dto)
    {
        
        if(string.IsNullOrWhiteSpace(dto.PhoneNumber) || string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest("Phonw number and code are required");
        }

        var (success, error) = await _otpService.VerifyOtpAsync(dto.PhoneNumber, dto.Code);

        if(!success)
             return Unauthorized(error);

        //Get user and assign a jwt token
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.PhoneNumber);
        if(user is null)
                return Unauthorized("User not found");

        var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber
        });
    }
}