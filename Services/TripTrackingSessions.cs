namespace Hopon.Api.Services;

// Hopon runs on one server at demo scale. Keep joined connections so voiding also revokes an open map.
public class TripTrackingSessions
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(int UserId, int TripId), HashSet<string>> _connections = new();

    public async Task JoinAsync(int userId, int tripId, string connectionId, Func<Task> authorizeAndJoin)
    {
        await _gate.WaitAsync();
        try
        {
            await authorizeAndJoin();
            var key = (userId, tripId);
            if (!_connections.TryGetValue(key, out var connections))
                _connections[key] = connections = new HashSet<string>();
            connections.Add(connectionId);
        }
        finally { _gate.Release(); }
    }

    public async Task RevokeAsync(int userId, int tripId, Func<string, Task> removeFromGroup)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_connections.TryGetValue((userId, tripId), out var connections)) return;
            foreach (var connection in connections) await removeFromGroup(connection);
            _connections.Remove((userId, tripId));
        }
        finally { _gate.Release(); }
    }

    public async Task LeaveAsync(string connectionId, int? tripId = null)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var key in _connections.Keys.Where(k => tripId == null || k.TripId == tripId).ToList())
            {
                _connections[key].Remove(connectionId);
                if (_connections[key].Count == 0) _connections.Remove(key);
            }
        }
        finally { _gate.Release(); }
    }
}
