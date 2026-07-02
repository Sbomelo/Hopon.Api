using Microsoft.AspNetCore.Authentication;

namespace Hopon.Api.Models;

public class User
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public EmergencyContact? EmergencyContact { get; set; }
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
}