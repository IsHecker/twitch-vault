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

public sealed class StorageCapacityGate(long? capacityBytes)
{
    private long _used;

    public bool HasCapacityFor(long size) => capacityBytes is not { } cap || cap - Interlocked.Read(ref _used) >= size;

    public bool TryReserve(long size)
    {
        if (capacityBytes is not { } cap) { Interlocked.Add(ref _used, size); return true; }
        while (true)
        {
            var current = Interlocked.Read(ref _used);
            if (cap - current < size) return false;
            if (Interlocked.CompareExchange(ref _used, current + size, current) == current) return true;
        }
    }

    public void Release(long size) => Interlocked.Add(ref _used, -size);
}