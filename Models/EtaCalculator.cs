using Hopon.Api.Models.Enums;

namespace Hopon.Api.Models
{
    public static class EtaCalculator
    {
        public static (DateTime? EstimatedArrival, bool IsEstimate) CalculateStopEta(Trip trip, TripStop stop)
        {
            if (stop.Status is TripStopStatus.Arrived or TripStopStatus.Departed)
                return (stop.ActualArrival, false);
            if (trip.Status is TripStatus.Cancelled or TripStatus.Completed)
                return (null, false);

            var departureDelay = trip.ActualDeparture.HasValue
                ? trip.ActualDeparture.Value - trip.ScheduledDeparture
                : TimeSpan.Zero;
            return (stop.ScheduledArrival + departureDelay, true);
        }

        public static (DateTime? EstimatedArrival, bool IsEstimate) CalculateEta (Trip trip)
        {
            switch (trip.Status)
            {
                case TripStatus.Cancelled:
                    return (null, false);

                case TripStatus.Completed:
                    return (trip.ActualArrival, false);

                default:
                    if(trip.ActualDeparture is null)
                    {
                        return (trip.ScheduledArrival, true);
                    }

                    var departureDelay = trip.ActualDeparture.Value - trip.ScheduledDeparture;
                    var estimatedArrival = trip.ScheduledArrival + departureDelay;

                    return (estimatedArrival, true);
            }
                
        }
    }
}
