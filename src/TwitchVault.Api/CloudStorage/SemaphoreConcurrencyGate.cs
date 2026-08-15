namespace TwitchVault.Api.CloudStorage;

public sealed class SemaphoreConcurrencyGate
{
    private readonly SemaphoreSlim _semaphore;

    public SemaphoreConcurrencyGate(int maxConcurrency)
    {
        maxConcurrency = Math.Max(1, maxConcurrency);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public bool TryAcquire() => _semaphore.Wait(0);

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => await _semaphore.WaitAsync(timeout, cancellationToken);

    public void Release() => _semaphore.Release();
}