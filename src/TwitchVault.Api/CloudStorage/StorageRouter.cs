using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageRouter(
    StorageProviderRegistry registry,
    IStorageRoutingStrategy strategy)
{
    public Task<Result<StorageSession>> AcquireSessionAsync(long sizeBytes, CancellationToken cancellationToken) =>
        AcquireSessionAsync(registry.EnabledInstances, sizeBytes, cancellationToken);

    public async Task<Result<StorageSession>> AcquireSessionAsync(
        string instanceName, long sizeBytes, CancellationToken cancellationToken)
    {
        var instance = registry.GetInstance(instanceName);
        return await AcquireSessionAsync([instance], sizeBytes, cancellationToken);
    }

    public async Task<Result<StorageSession>> AcquireSessionAsync(
        IReadOnlyList<ManagedStorageInstance> pool, long sizeBytes, CancellationToken cancellationToken)
    {
        if (pool.Count == 0)
            return Error.Failure("NoProvidersAvailable", "No storage providers are configured or enabled.");

        var eligible = pool.Where(i => i.Capacity.HasCapacityFor(sizeBytes)).ToList();
        if (eligible.Count == 0)
            return Error.Failure("InsufficientCapacity", $"No provider has enough capacity for this payload.");

        using var queueTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        queueTimeoutCts.CancelAfter(TimeSpan.FromMinutes(1));

        var ordered = strategy.Order(eligible);
        var acquireTasks = ordered.Select(async instance =>
        {
            var lease = await instance.AcquireAsync(queueTimeoutCts.Token);
            return (instance, lease);
        }).ToList();

        while (acquireTasks.Count > 0)
        {
            var finished = await Task.WhenAny(acquireTasks);
            acquireTasks.Remove(finished);
            var (instance, lease) = await finished;

            if (!lease.IsAcquired)
                continue;

            queueTimeoutCts.Cancel();
            if (instance.Capacity.TryReserve(sizeBytes))
                return new StorageSession(instance, lease, sizeBytes);

            lease.Dispose();
            continue;
        }

        if (queueTimeoutCts.IsCancellationRequested)
            return Error.Failure("UploadCancelled", "Timed out waiting for an available upload slot across all providers.");

        return Error.Failure("SlotAcquisitionFailed", "All upload concurrency queues are full or timed out.");
    }
}