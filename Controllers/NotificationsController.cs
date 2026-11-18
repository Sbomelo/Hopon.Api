using Hopon.Api.Services.Interfaces;
using Hopon.Api.Services;
using Hopon.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hopon.Api.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    [Authorize(Roles = "Passenger")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly AccessAuditService _audit;

        public NotificationsController(INotificationService notificationService, AccessAuditService audit)
        {
            _notificationService = notificationService;
            _audit = audit;
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var notifications = await _notificationService.GetNotificationsForUserAsync(userId);
            await _audit.RecordAsync(userId, null, AccessAction.ViewedNotifications);
            return Ok(notifications);
        }
    }
}
