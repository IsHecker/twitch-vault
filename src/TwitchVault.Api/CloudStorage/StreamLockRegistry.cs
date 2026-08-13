using System.Collections.Concurrent;

namespace TwitchVault.Api.CloudStorage;

public sealed class StreamLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<IAsyncDisposable?> TryAcquireLockAsync(
        string streamId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(streamId))
            return null;

        var semaphore = _locks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(timeout, cancellationToken))
            return null;

        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}