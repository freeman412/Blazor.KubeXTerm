using System.Collections.Concurrent;

namespace Blazor.KubeXTerm.Services;

public class KubeXTermSessionRegistry
{
    public readonly ConcurrentDictionary<Guid, KubeXTermConnectionManager> _sessions = new();

    public KubeXTermConnectionManager GetOrCreate(Guid sessionId, Func<KubeXTermConnectionManager> factory, out bool isNew)
    {
        // Try fast path
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            isNew = false;
            return existing;
        }

        // Create and add or return existing
        var created = factory();
        if (_sessions.TryAdd(sessionId, created))
        {
            isNew = true;
            return created;
        }

        isNew = false;
        return _sessions[sessionId];
    }

    public bool TryGet(Guid sessionId, out KubeXTermConnectionManager manager) => _sessions.TryGetValue(sessionId, out manager);

    public async Task CloseAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var mgr))
        {
            await mgr.DisposeAsync();
        }
    }
}