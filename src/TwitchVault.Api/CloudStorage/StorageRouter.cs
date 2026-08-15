using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageRouter(
    StorageProviderRegistry registry,
    IStorageRoutingStrategy strategy,
    ILogger<StorageRouter> logger)
{
    public Task<Result<StorageUploadSession>> AcquireSessionAsync(long sizeBytes, CancellationToken cancellationToken) =>
        AcquireSessionAsync(registry.EnabledInstances, sizeBytes, cancellationToken);

    public async Task<Result<StorageUploadSession>> AcquireSessionAsync(
        string instanceName, long sizeBytes, CancellationToken cancellationToken)
    {
        var instance = registry.GetInstance(instanceName);
        return await AcquireSessionAsync([instance], sizeBytes, cancellationToken);
    }

    private async Task<Result<StorageUploadSession>> AcquireSessionAsync(
        IReadOnlyList<ManagedStorageInstance> pool, long sizeBytes, CancellationToken cancellationToken)
    {
        var eligible = new List<ManagedStorageInstance>();

        foreach (var instance in pool)
        {
            if (!instance.Capacity.HasCapacityFor(sizeBytes))
                continue;

            eligible.Add(instance);
        }

        if (eligible.Count == 0)
        {
            return pool.Count == 1
                ? Error.Failure($"Storage instance '{pool[0].Provider.Options.Name}' is unavailable or lacks capacity for {sizeBytes} bytes.")
                : Error.Failure("No healthy storage provider is available with sufficient capacity.");
        }

        var ordered = strategy.Order(eligible);
        foreach (var candidate in ordered)
        {
            if (!candidate.ConcurrencySlot.TryAcquire())
                continue;

            logger.LogDebug("StorageRouter selected provider '{Name}' for payload of {Size} bytes.",
                candidate.Provider.Options.Name, sizeBytes);

            return new StorageUploadSession(candidate);
        }

        var head = ordered[0];
        var timeout = TimeSpan.FromSeconds(Math.Max(1, head.Provider.Options.Behavior.RequestTimeoutSeconds));

        logger.LogInformation(
            "All eligible storage providers are at their concurrency limit; waiting up to {Timeout}s for '{Name}'.",
            timeout, head.Provider.Options.Name);

        if (!await head.ConcurrencySlot.WaitAsync(timeout, cancellationToken))
            return Error.Failure($"Timed out waiting for an available upload slot on '{head.Provider.Options.Name}'.");

        return new StorageUploadSession(head);
    }
}