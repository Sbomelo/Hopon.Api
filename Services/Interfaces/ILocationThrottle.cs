using Hopon.Api.Models;
namespace Hopon.Api.Services.Interfaces;

public interface ILocationThrottle
{
    LocationUpdate? Latest(int tripId);
    Task<LocationRecordResult> RecordAsync(int tripId, double latitude, double longitude,
        Func<Task<LocationUpdate?>> loadPrevious, Func<LocationUpdate, Task> persistFix, Func<LocationUpdate, Task> broadcast,
        bool simulatorJump = false);
}
