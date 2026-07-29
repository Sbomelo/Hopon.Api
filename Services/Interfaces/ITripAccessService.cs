namespace Hopon.Api.Services
{
    public interface ITripAccessService
    {
        Task<TripAccessResult> CheckTripAccessAsync(int userId, int tripId);
    }
}
