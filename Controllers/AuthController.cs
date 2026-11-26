using Hopon.Api.Data;
using Hopon.Api.DTOs.Auth;
using Hopon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
        if (!PhoneNumberNormalizer.TryNormalizeSouthAfrican(dto.PhoneNumber, out var phoneNumber))
        {
            return BadRequest("A valid South African mobile phone number is required.");
        }
                
        var (success, error, devOnlyCode) = await _otpService.RequestOtpAsync(phoneNumber);

        if(!success)
            return BadRequest(error);

        return Ok(new {message = "OTP sent", devOnlyCode});
    }

    [HttpPost("otp/verify")]
    public async Task<ActionResult<AuthResponseDto>> VerifyOtp ([FromBody] VerifyOtpDto dto)
    {
        
        if(string.IsNullOrWhiteSpace(dto.PhoneNumber) || string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest("Phone number and code are required.");
        }

        if (!PhoneNumberNormalizer.TryNormalizeSouthAfrican(dto.PhoneNumber, out var phoneNumber))
            return BadRequest("A valid South African mobile phone number is required.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.IsActive);
        if(user is null)
                return Unauthorized("Unable to process this request.");

        var (success, error) = await _otpService.VerifyOtpAsync(phoneNumber, dto.Code);
        if (!success) return Unauthorized(error);

        return Ok(CreateAuthResponse(user));
    }

    [Authorize(Roles = "Passenger")]
    [HttpGet("profile")]
    public ActionResult<PassengerProfileDto> Profile()
    {
        return Ok(new PassengerProfileDto(User.Identity!.Name ?? string.Empty, User.FindFirstValue("phoneNumber")!));
    }

    [Authorize(Roles = "Passenger")]
    [HttpPost("phone-change/request")]
    public async Task<IActionResult> RequestPhoneChange(RequestOtpDto dto)
    {
        if (!PhoneNumberNormalizer.TryNormalizeSouthAfrican(dto.PhoneNumber, out var newPhone))
            return BadRequest("A valid South African mobile phone number is required.");

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentPhone = User.FindFirstValue("phoneNumber")!;
        var (success, error, devOnlyCode) = await _otpService.RequestPhoneChangeAsync(userId, currentPhone, newPhone);
        return success ? Ok(new { message = "OTP sent", devOnlyCode }) : BadRequest(error);
    }

    [Authorize(Roles = "Passenger")]
    [HttpPost("phone-change/verify")]
    public async Task<ActionResult<AuthResponseDto>> VerifyPhoneChange(VerifyOtpDto dto)
    {
        if (!PhoneNumberNormalizer.TryNormalizeSouthAfrican(dto.PhoneNumber, out var newPhone))
            return BadRequest("A valid South African mobile phone number is required.");
        if (dto.Code is not { Length: 6 } || !dto.Code.All(char.IsAsciiDigit))
            return BadRequest("Enter the six-digit code.");

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentPhone = User.FindFirstValue("phoneNumber")!;
        var (user, error) = await _otpService.ChangePhoneAsync(userId, currentPhone, newPhone, dto.Code);
        if (user is null) return BadRequest(error);

        return Ok(CreateAuthResponse(user));
    }

    private AuthResponseDto CreateAuthResponse(Models.User user)
    {
        var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber
        };
    }
}
