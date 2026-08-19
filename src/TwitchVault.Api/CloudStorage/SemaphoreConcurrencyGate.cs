namespace TwitchVault.Api.CloudStorage;

// public sealed class SemaphoreConcurrencyGate
// {
//     private readonly SemaphoreSlim _semaphore;

//     public SemaphoreConcurrencyGate(int maxConcurrency)
//     {
//         maxConcurrency = Math.Max(1, maxConcurrency);
//         _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
//     }

//     public bool TryAcquire() => _semaphore.Wait(0);

//     public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
//         => await _semaphore.WaitAsync(timeout, cancellationToken);

//     public void Release() => _semaphore.Release();
// }

public sealed class ResizableConcurrencyGate(int initialMax)
{
    private readonly SemaphoreSlim _wakeSignal = new(0);
    private int _inFlight;
    private int _maxConcurrency = Math.Max(1, initialMax);

    public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);
    public int InFlight => Volatile.Read(ref _inFlight);

    public void UpdateMaxConcurrency(int newMax)
    {
        var normalized = Math.Max(1, newMax);
        var previous = Interlocked.Exchange(ref _maxConcurrency, normalized);

        // If the limit just grew, nudge any waiters to re-check rather than making them wait
        // out their full timeout for room that already exists.
        if (normalized > previous && _wakeSignal.CurrentCount == 0)
            _wakeSignal.Release();
    }

    public bool TryAcquire()
    {
        while (true)
        {
            var current = InFlight;
            if (current >= MaxConcurrency)
                return false;

            if (Interlocked.CompareExchange(ref _inFlight, current + 1, current) == current)
                return true;
        }
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (TryAcquire())
            return true;

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return false;

            await _wakeSignal.WaitAsync(remaining, cancellationToken);

            if (TryAcquire())
                return true;
        }
    }

    public void Release()
    {
        Interlocked.Decrement(ref _inFlight);
        if (_wakeSignal.CurrentCount == 0)
            _wakeSignal.Release();
    }
}