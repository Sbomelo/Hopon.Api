using System.Globalization;
using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Notifications;

public static class NotificationMessages
{
    public static string Reminder(NotificationType type, string stopName, DateTime stopEta)
    {
        var time = SaTime.ToSa(stopEta).ToString("HH:mm", CultureInfo.InvariantCulture);
        if (type == NotificationType.TenMinutesBefore)
            return $"Your bus is about 10 minutes away — get ready to get off at {stopName} (arriving around {time}).";

        var remaining = type switch
        {
            NotificationType.FourHoursBefore => "4 hours",
            NotificationType.TwoHoursBefore => "2 hours",
            NotificationType.ThirtyMinutesBefore => "30 minutes",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Expected a reminder type.")
        };
        return $"Your bus is about {remaining} from {stopName} (arriving around {time}).";
    }

    public static string Arrival(string stopName) => $"You have arrived at {stopName}.";
}
