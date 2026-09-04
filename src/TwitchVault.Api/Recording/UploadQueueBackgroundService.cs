using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Recording;

public sealed class UploadQueueBackgroundService(
    IUploadQueue uploadQueue,
    IStreamStorageService storageService,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<UploadQueueBackgroundService> logger) : BackgroundService
{
    private sealed record Worker(Task Task, CancellationTokenSource Cts);

    private const int MaxUploadAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<int, Worker> _activeWorkers = new();

    // Every worker that has been started but hasn't fully stopped yet. A worker is removed
    // from _activeWorkers as soon as it's scaled down, but it keeps running (and stays in
    // _allWorkers) until it observes cancellation and exits. Shutdown waits on this set so
    // it doesn't abandon workers that were mid-flight when a scale-down happened.
    private readonly ConcurrentDictionary<int, Worker> _allWorkers = new();

    private readonly object _scaleLock = new();
    private int _nextWorkerId;
    private IDisposable? _optionsChangeSubscription;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe before applying the initial value, so a config change landing between
        // the two can never be missed. AdjustWorkerCount is idempotent (a no-op if the pool
        // is already at the target size), so an occasional duplicate call is harmless.
        _optionsChangeSubscription = vaultOptions.OnChange(options =>
            AdjustWorkerCount(options.MaxConcurrentUploadWorkers, stoppingToken));

        AdjustWorkerCount(vaultOptions.CurrentValue.MaxConcurrentUploadWorkers, stoppingToken);

        await WaitForStopAsync(stoppingToken);
        await DrainWorkersAsync();
    }

    private void AdjustWorkerCount(int targetCount, CancellationToken stoppingToken)
    {
        lock (_scaleLock)
        {
            var currentCount = _activeWorkers.Count;
            if (targetCount > currentCount)
            {
                var workersToAdd = targetCount - currentCount;
                logger.LogInformation("Scaling up upload worker pool: adding {Count} worker(s) (target: {Target}).", workersToAdd, targetCount);

                for (var i = 0; i < workersToAdd; i++)
                {
                    StartWorker(stoppingToken);
                }
            }
            else if (targetCount < currentCount)
            {
                var workersToRemove = currentCount - targetCount;
                logger.LogInformation("Scaling down upload worker pool: removing {Count} worker(s) (target: {Target}).", workersToRemove, targetCount);

                foreach (var workerId in _activeWorkers.Keys.Take(workersToRemove))
                {
                    if (_activeWorkers.TryRemove(workerId, out var worker))
                        worker.Cts.Cancel();
                }
            }
        }
    }

    private void StartWorker(CancellationToken stoppingToken)
    {
        var workerId = Interlocked.Increment(ref _nextWorkerId);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var task = Task.Run(() => ProcessQueueAsync(workerId, cts.Token), cts.Token);

        var worker = new Worker(task, cts);
        _activeWorkers[workerId] = worker;
        _allWorkers[workerId] = worker;
    }

    private async Task ProcessQueueAsync(int workerId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Upload worker #{WorkerId} started.", workerId);

        try
        {
            await foreach (var batch in uploadQueue.ReadAllAsync(cancellationToken))
            {
                await ProcessBatchWithRetryAsync(workerId, batch, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            logger.LogError(ex, "Unhandled exception in upload worker #{WorkerId}.", workerId);
        }
        finally
        {
            _activeWorkers.TryRemove(workerId, out _);

            if (_allWorkers.TryRemove(workerId, out var worker))
                worker.Cts.Dispose();

            logger.LogInformation("Upload worker #{WorkerId} stopped.", workerId);
        }
    }

    private async Task ProcessBatchWithRetryAsync(int workerId, UploadBatch batch, CancellationToken cancellationToken)
    {
        try
        {
            var succeeded = false;
            var retryDelay = InitialRetryDelay;

            for (var attempt = 1; attempt <= MaxUploadAttempts && !cancellationToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    succeeded = await storageService.UploadBatchAsync(batch.LocalFilePaths, batch.Stream, CancellationToken.None);
                    if (succeeded)
                        break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Worker #{WorkerId}: Exception uploading batch for stream '{StreamId}' (attempt {Attempt}/{MaxAttempts}).",
                        workerId, batch.Stream.Id, attempt, MaxUploadAttempts);
                }

                var isLastAttempt = attempt == MaxUploadAttempts;
                if (isLastAttempt || !await TryDelayAsync(retryDelay, cancellationToken))
                    break;

                retryDelay *= 2;
            }
        }
        finally
        {
            batch.OnCompleted?.Invoke();
        }
    }

    private static async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitForStopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task DrainWorkersAsync()
    {
        logger.LogInformation("Stopping upload queue: waiting for active workers to finish...");

        foreach (var worker in _allWorkers.Values)
        {
            worker.Cts.Cancel();
        }

        try
        {
            await Task.WhenAll(_allWorkers.Values.Select(w => w.Task)).WaitAsync(ShutdownDrainTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Timed out or failed waiting for upload workers to finish within {Timeout}.", ShutdownDrainTimeout);
        }
    }

    public override void Dispose()
    {
        _optionsChangeSubscription?.Dispose();
        foreach (var worker in _allWorkers.Values)
        {
            worker.Cts.Dispose();
        }

        base.Dispose();
    }
}