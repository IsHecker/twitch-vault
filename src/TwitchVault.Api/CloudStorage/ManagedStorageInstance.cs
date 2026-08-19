using System.Threading.RateLimiting;

namespace TwitchVault.Api.CloudStorage;

public sealed class ManagedStorageInstance(
    ICloudStorageProvider provider,
    StorageCapacityGate capacity)
{
    public ICloudStorageProvider Provider { get; } = provider;
    public StorageCapacityGate Capacity { get; } = capacity;
    private RateLimiter _limiter = CreateLimiter(
        provider.Options.Behavior.MaxConcurrentUploads,
        provider.Options.Behavior.QueueLimit);

    private static ConcurrencyLimiter CreateLimiter(int maxConcurrent, int queueLimit) =>
        new(new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, maxConcurrent),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

    public void ApplyBehaviorUpdate(StorageBehaviorOptions behavior)
    {
        var old = _limiter;
        _limiter = CreateLimiter(behavior.MaxConcurrentUploads, behavior.QueueLimit);
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ => old.Dispose());
    }

    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken ct) => _limiter.AcquireAsync(1, ct);
}