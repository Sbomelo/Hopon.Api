namespace Hopon.Api.Models.Enums
{
    public static class TripStatusRules
    {
        public static bool IsTrackingActive(TripStatus status)
        {
            return status is TripStatus.Scheduled or TripStatus.InProgress or TripStatus.Delayed;
        }

    }
}