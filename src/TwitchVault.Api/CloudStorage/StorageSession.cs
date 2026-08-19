using System.Threading.RateLimiting;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageSession(
    ManagedStorageInstance storageInstance,
    RateLimitLease lease,
    long reservedBytes) : IDisposable
{
    private int _released;
    public ICloudStorageProvider Provider => storageInstance.Provider;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            lease.Dispose();
            if (reservedBytes > 0)
                storageInstance.Capacity.Release(reservedBytes);
        }
    }
}