using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Notifications;

public sealed class NotificationOptions
{
    public bool SmsEnabled { get; set; }
    public string[] SmsTypes { get; set; } =
    [
        nameof(NotificationType.ThirtyMinutesBefore), nameof(NotificationType.TenMinutesBefore),
        nameof(NotificationType.Arrival), nameof(NotificationType.Delay), nameof(NotificationType.Emergency)
    ];

    public bool SendsSms(NotificationType type) => SmsEnabled && SmsTypes.Contains(type.ToString(), StringComparer.OrdinalIgnoreCase);

    public bool HasValidTypes() => SmsTypes.All(name => Enum.GetNames<NotificationType>().Contains(name, StringComparer.OrdinalIgnoreCase));
}
