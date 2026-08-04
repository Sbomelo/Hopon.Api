using Hopon.Api.Models.Enums;

namespace Hopon.Api.Models
{
    public static class EtaCalculator
    {
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
