namespace TwitchVault.Api.CloudStorage;

public sealed class StorageCapacityTracker(long? capacityBytes)
{
    private long _usedBytes;

    public long? CapacityBytes => capacityBytes;

    public long UsedBytes => Interlocked.Read(ref _usedBytes);

    public long? AvailableBytes => CapacityBytes is { } total ? Math.Max(0, total - UsedBytes) : null;

    public bool HasCapacityFor(long sizeBytes) => AvailableBytes is not { } available || available >= sizeBytes;

    public void AllocateCapacity(long sizeBytes) => Interlocked.Add(ref _usedBytes, sizeBytes);

    public void ReleaseCapacity(long sizeBytes) => Interlocked.Add(ref _usedBytes, -sizeBytes);
}