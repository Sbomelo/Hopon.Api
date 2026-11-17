using Hopon.Api.Models.Enums;

namespace Hopon.Api.Services.Notifications;

public static class NotificationThresholds
{
    private static readonly (NotificationType Type, TimeSpan Before)[] Thresholds =
    {
        (NotificationType.TenMinutesBefore, TimeSpan.FromMinutes(10)),
        (NotificationType.ThirtyMinutesBefore, TimeSpan.FromMinutes(30)),
        (NotificationType.TwoHoursBefore, TimeSpan.FromHours(2)),
        (NotificationType.FourHoursBefore, TimeSpan.FromHours(4))
    };

    public static NotificationType? GetDue(DateTime now, DateTime stopEta, IReadOnlySet<NotificationType> alreadySent)
    {
        var remaining = stopEta - now;
        if (remaining < TimeSpan.Zero)
            return null;

        for (var index = 0; index < Thresholds.Length; index++)
        {
            if (remaining > Thresholds[index].Before)
                continue;

            // The most urgent crossed threshold also satisfies all less urgent ones.
            for (var urgent = 0; urgent <= index; urgent++)
                if (alreadySent.Contains(Thresholds[urgent].Type))
                    return null;

            return Thresholds[index].Type;
        }

        return null;
    }
}
