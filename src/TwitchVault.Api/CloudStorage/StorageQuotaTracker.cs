using System.Collections.Concurrent;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageQuotaTracker
{
    private readonly ConcurrentDictionary<string, long?> _streams = new();

    public StorageQuotaTracker(StorageProviderRegistry providerRegistry)
    {
        foreach (var instance in providerRegistry.EnabledInstances)
        {
            _streams[instance.Options.Name] = instance.Options.Behavior.CapacityBytes;
        }
    }

    public void Allocate(string instanceName, long sizeBytes)
    {
        _streams[instanceName] -= sizeBytes;
    }

    public void Release(string instanceName, long sizeBytes)
    {
        _streams[instanceName] += sizeBytes;
    }

    public bool HasCapacity(string instanceName, long sizeBytes)
    {
        if (_streams[instanceName] is null)
            return true; // unlimited

        return _streams[instanceName] - sizeBytes > 0;
    }
}