using Hopon.Api.Models;
namespace Hopon.Api.Services.Interfaces;

public interface ILocationThrottle
{
    Task<LocationRecordResult> RecordAsync(int tripId, double latitude, double longitude,
        Func<Task<LocationUpdate?>> loadPrevious, Func<LocationUpdate, Task> persistFix, Func<LocationUpdate, Task> broadcast);
}
