using Hopon.Api.Models.Enums;
using Hopon.Api.Services.Notifications;
using Xunit;

namespace Hopon.Api.Tests;

public class NotificationThresholdTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<object?[]> BoundaryCases()
    {
        yield return new object?[] { -1L, null };
        yield return new object?[] { 0L, NotificationType.TenMinutesBefore };
        foreach (var (minutes, at, after) in new[]
        {
            (10, NotificationType.TenMinutesBefore, (NotificationType?)NotificationType.ThirtyMinutesBefore),
            (30, NotificationType.ThirtyMinutesBefore, (NotificationType?)NotificationType.TwoHoursBefore),
            (120, NotificationType.TwoHoursBefore, (NotificationType?)NotificationType.FourHoursBefore),
            (240, NotificationType.FourHoursBefore, (NotificationType?)null)
        })
        {
            var ticks = TimeSpan.FromMinutes(minutes).Ticks;
            yield return new object?[] { ticks - 1, at };
            yield return new object?[] { ticks, at };
            yield return new object?[] { ticks + 1, after };
        }
    }

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public void Exact_boundaries_catch_up_to_only_the_most_urgent_crossed_threshold(long remainingTicks, NotificationType? expected)
    {
        Assert.Equal(expected, NotificationThresholds.GetDue(Now, Now.AddTicks(remainingTicks), new HashSet<NotificationType>()));
    }

    public static IEnumerable<object?[]> SentCombinations()
    {
        // Bits: four hours, two hours, thirty minutes, ten minutes.
        foreach (var (minutes, due, suppressors) in new[]
        {
            (240, NotificationType.FourHoursBefore, 0b1111),
            (180, NotificationType.FourHoursBefore, 0b1111),
            (120, NotificationType.TwoHoursBefore, 0b1110),
            (60, NotificationType.TwoHoursBefore, 0b1110),
            (30, NotificationType.ThirtyMinutesBefore, 0b1100),
            (20, NotificationType.ThirtyMinutesBefore, 0b1100),
            (10, NotificationType.TenMinutesBefore, 0b1000),
            (0, NotificationType.TenMinutesBefore, 0b1000)
        })
            for (var mask = 0; mask < 16; mask++)
                yield return new object?[] { minutes, mask, (mask & suppressors) == 0 ? due : (NotificationType?)null };
    }

    [Theory]
    [MemberData(nameof(SentCombinations))]
    public void Every_already_sent_combination_suppresses_equal_or_less_urgent_reminders(int minutes, int mask, NotificationType? expected)
    {
        var types = new[] { NotificationType.FourHoursBefore, NotificationType.TwoHoursBefore,
            NotificationType.ThirtyMinutesBefore, NotificationType.TenMinutesBefore };
        var sent = types.Where((_, bit) => (mask & (1 << bit)) != 0).ToHashSet();
        Assert.Equal(expected, NotificationThresholds.GetDue(Now, Now.AddMinutes(minutes), sent));
    }

    [Fact]
    public void Delay_and_emergency_logs_do_not_satisfy_reminder_thresholds()
    {
        var sent = new HashSet<NotificationType> { NotificationType.Delay, NotificationType.Emergency };
        Assert.Equal(NotificationType.ThirtyMinutesBefore, NotificationThresholds.GetDue(Now, Now.AddMinutes(25), sent));
        Assert.Null(NotificationThresholds.GetDue(Now, Now.AddDays(-1), sent));
        Assert.Null(NotificationThresholds.GetDue(Now, Now.AddDays(1), sent));
    }

    [Theory]
    [InlineData(NotificationType.FourHoursBefore, "4 hours")]
    [InlineData(NotificationType.TwoHoursBefore, "2 hours")]
    [InlineData(NotificationType.ThirtyMinutesBefore, "30 minutes")]
    [InlineData(NotificationType.TenMinutesBefore, "10 minutes")]
    public void Messages_include_stop_and_sast_eta(NotificationType type, string duration)
    {
        var message = NotificationMessages.Reminder(type, "Harrismith", new DateTime(2026, 11, 20, 19, 40, 0, DateTimeKind.Utc));
        Assert.Contains("about " + duration, message);
        Assert.Contains("Harrismith", message);
        Assert.Contains("21:40", message);
        if (type == NotificationType.TenMinutesBefore)
            Assert.Contains("get ready to get off at Harrismith", message);
        Assert.Equal("You have arrived at Johannesburg Park Station.", NotificationMessages.Arrival("Johannesburg Park Station"));
    }
}
