namespace TwitchVault.Api.CloudStorage;

public sealed class StorageUploadSession(ManagedStorageInstance instance) : IDisposable
{
    private int _released;

    public ManagedStorageInstance Instance => instance;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            Instance.ConcurrencySlot.Release();
    }
}