using System.Threading.Channels;
using System.Threading.RateLimiting;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageRouter(StorageProviderRegistry registry, ILogger<StorageRouter> logger)
{
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromMinutes(1);

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

        var channel = Channel.CreateUnbounded<(ManagedStorageInstance Instance, RateLimitLease Lease)>();

        try
        {
            using var queueTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queueTimeoutCts.CancelAfter(QueueTimeout);

            var racers = eligible.Select(i => AcquireLeaseAsync(i, channel.Writer, queueTimeoutCts.Token)).ToArray();
            _ = Task.WhenAll(racers)
                .ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

            StorageSession? winner = null;
            await foreach (var (instance, lease) in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                if (winner is not null || !instance.Capacity.TryReserve(sizeBytes))
                {
                    lease.Dispose();
                    continue;
                }

                queueTimeoutCts.Cancel();
                winner = new StorageSession(instance, lease, sizeBytes);
            }

            if (winner is not null)
                return winner;
        }
        catch (Exception ex)
        {
            return Error.Failure(ex.Message);
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        return Error.Failure("SlotAcquisitionFailed", "All upload concurrency queues are full or timed out.");
    }

    private async Task AcquireLeaseAsync(
        ManagedStorageInstance instance,
        ChannelWriter<(ManagedStorageInstance, RateLimitLease)> writer,
        CancellationToken token)
    {
        RateLimitLease? lease = null;
        try
        {
            lease = await instance.AcquireAsync(token);
            token.ThrowIfCancellationRequested();

            if (!lease.IsAcquired)
            {
                lease.Dispose();
                return;
            }

            await writer.WriteAsync((instance, lease), token);
        }
        catch (Exception ex)
        {
            lease?.Dispose();
            if (ex is OperationCanceledException)
                return;

            logger.LogError(ex, "Provider '{Instance}' failed to acquire permit", instance.Provider.Options.Name);
        }
    }
}