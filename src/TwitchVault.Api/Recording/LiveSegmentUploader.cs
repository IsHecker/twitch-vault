using TwitchVault.Api.Configuration;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Recording;

public interface ISegmentUploader : IDisposable
{
    void Attach(Domain.Stream stream);
    Task AddAsync(LocalSegment segment);
    Task FlushRemainingAsync();
}

public sealed class LiveSegmentUploader(
    IUploadQueue uploadQueue,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<LiveSegmentUploader> logger) : ISegmentUploader
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(120);

    private readonly List<LocalSegment> _buffer = [];
    private readonly object _lock = new();
    private readonly SemaphoreSlim _uploadsCompletedSignal = new(0, 1);
    private Action _onBatchCompleted = null!;

    private Domain.Stream _stream = null!;
    private int _pendingUploads;
    private bool _isDraining;
    private bool _disposed;

    public void Attach(Domain.Stream stream)
    {
        _stream = stream;
        _onBatchCompleted = HandleBatchCompleted;
        // ResetIdleTimer();
    }

    public async Task AddAsync(LocalSegment segment)
    {
        List<string>? batchToQueue = null;

        lock (_lock)
        {
            if (_disposed || _isDraining)
                return;

            _buffer.Add(segment);
            if (_buffer.Count >= vaultOptions.CurrentValue.UploadBatchSize)
                batchToQueue = ConsumeBuffer();
        }

        // ResetIdleTimer();
        if (batchToQueue is not null)
            await EnqueueBatchAsync(batchToQueue, isUrgent: false);
    }

    public async Task FlushRemainingAsync()
    {
        // CancelIdleTimer();
        List<string> remaining;
        lock (_lock)
        {
            remaining = ConsumeBuffer();
        }

        if (remaining.Count > 0)
            await EnqueueBatchAsync(remaining, isUrgent: true);

        lock (_lock)
        {
            if (Volatile.Read(ref _pendingUploads) == 0)
                return;

            _isDraining = true;
        }

        var drained = await _uploadsCompletedSignal.WaitAsync(DrainTimeout);
        if (!drained)
        {
            logger.LogWarning("Timed out after {Timeout} waiting for stream '{StreamId}' batches to upload.",
                DrainTimeout, _stream.Id);
        }
    }

    private async Task EnqueueBatchAsync(IReadOnlyList<string> filePaths, bool isUrgent)
    {
        Interlocked.Increment(ref _pendingUploads);
        await uploadQueue.QueueBatchAsync(new UploadBatch(_stream, filePaths, _onBatchCompleted, isUrgent));
    }

    private void HandleBatchCompleted()
    {
        if (Interlocked.Decrement(ref _pendingUploads) != 0)
            return;

        lock (_lock)
        {
            if (!_isDraining || _disposed)
                return;

            try
            {
                _uploadsCompletedSignal.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private List<string> ConsumeBuffer()
    {
        var count = _buffer.Count;
        var content = new List<string>(count);
        for (var i = 0; i < count; i++)
            content.Add(_buffer[i].FilePath);

        _buffer.Clear();
        return content;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _uploadsCompletedSignal.Dispose();
        }
    }

    // private void ResetIdleTimer()
    // {
    //     CancelIdleTimer();

    //     var timeoutSec = vaultOptions.CurrentValue.IdleFlushTimeoutSeconds;

    //     var cts = new CancellationTokenSource();
    //     _idleCts = cts;

    //     _ = Task.Run(async () =>
    //     {
    //         try
    //         {
    //             await Task.Delay(TimeSpan.FromSeconds(timeoutSec), cts.Token);
    //             await FlushIdleBufferAsync();
    //         }
    //         catch { }
    //     });
    // }

    // private async Task FlushIdleBufferAsync()
    // {
    //     List<string>? batchToQueue = null;
    //     lock (_lock)
    //     {
    //         if (_buffer.Count <= 0)
    //             return;

    //         batchToQueue = ConsumeBuffer();
    //     }

    //     if (batchToQueue is not null)
    //         await uploadQueue.QueueBatchAsync(new UploadBatch(_stream, batchToQueue));
    // }

    // private void CancelIdleTimer()
    // {
    //     try
    //     {
    //         _idleCts?.Cancel();
    //         _idleCts?.Dispose();
    //         _idleCts = null;
    //     }
    //     catch { }
    // }
}