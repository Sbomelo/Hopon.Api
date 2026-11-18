using Hopon.Api.Models;
using Hopon.Api.Models.Enums;
using Xunit;

namespace Hopon.Api.Tests;

public class EtaAndTrackingTests
{
    private static readonly DateTime Departure = new(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TripStatus.Scheduled, null, 0, true)]
    [InlineData(TripStatus.InProgress, 0, 0, true)]
    [InlineData(TripStatus.InProgress, -10, -10, true)]
    [InlineData(TripStatus.Delayed, 25, 25, true)]
    [InlineData(TripStatus.Delayed, null, 0, true)]
    [InlineData(TripStatus.Completed, 25, 45, false)]
    [InlineData(TripStatus.Cancelled, 25, null, false)]
    public void Trip_eta_uses_schedule_offset_or_the_terminal_outcome(
        TripStatus status, int? departureOffset, int? expectedArrivalOffset, bool isEstimate)
    {
        var scheduledArrival = Departure.AddHours(8);
        var trip = new Trip
        {
            Status = status,
            ScheduledDeparture = Departure,
            ScheduledArrival = scheduledArrival,
            ActualDeparture = departureOffset is int minutes ? Departure.AddMinutes(minutes) : null,
            ActualArrival = status == TripStatus.Completed ? scheduledArrival.AddMinutes(45) : null
        };
        DateTime? expectedArrival = expectedArrivalOffset is int offset ? scheduledArrival.AddMinutes(offset) : null;

        Assert.Equal((expectedArrival, isEstimate), EtaCalculator.CalculateEta(trip));
    }

    [Theory]
    [InlineData(TripStatus.Scheduled, true)]
    [InlineData(TripStatus.InProgress, true)]
    [InlineData(TripStatus.Delayed, true)]
    [InlineData(TripStatus.Completed, false)]
    [InlineData(TripStatus.Cancelled, false)]
    public void Only_live_statuses_allow_tracking(TripStatus status, bool active)
    {
        Assert.Equal(active, TripStatusRules.IsTrackingActive(status));
    }
}
