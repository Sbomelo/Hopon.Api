namespace Hopon.Api.Models.Enums
{
    public static class TripStatusRules
    {
        public static bool CanTransition(TripStatus from, TripStatus to)
        {
            return (from, to) switch
            {
                (TripStatus.Scheduled, TripStatus.InProgress or TripStatus.Cancelled) => true,
                (TripStatus.InProgress, TripStatus.Delayed or TripStatus.Completed or TripStatus.Cancelled) => true,
                (TripStatus.Delayed, TripStatus.InProgress or TripStatus.Completed or TripStatus.Cancelled) => true,
                _ => false
            };
        }

        public static bool IsTrackingActive(TripStatus status)
        {
            return status is TripStatus.Scheduled or TripStatus.InProgress or TripStatus.Delayed;
        }

    }
}
