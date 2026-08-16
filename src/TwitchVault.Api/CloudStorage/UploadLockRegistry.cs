using System.Collections.Concurrent;

namespace TwitchVault.Api.CloudStorage;

// public sealed class StreamLockRegistry
// {
//     private sealed class StreamUploadState
//     {
//         public CancellationTokenSource Cts { get; } = new();
//         public SemaphoreSlim StateLock { get; } = new(1, 1);

//     }

//     private readonly ConcurrentDictionary<string, StreamUploadState> _states = new(StringComparer.OrdinalIgnoreCase);

//     public async ValueTask<IAsyncDisposable?> TryAcquireLockAsync(
//         string streamId,
//         CancellationToken cancellationToken)
//     {
//         if (string.IsNullOrWhiteSpace(streamId))
//             return null;

//         var state = _states.GetOrAdd(streamId, _ => new StreamUploadState());
//         if (!await state.StateLock.WaitAsync(TimeSpan.FromMinutes(10), cancellationToken))
//             return null;

//         return new Releaser(streamId, state, _states);
//     }

//     public CancellationTokenSource GetTokenSource(string streamId)
//         => _states[streamId].Cts;

//     public void Remove(string streamId) => _states.TryRemove(streamId, out _);

//     private sealed class Releaser(
//         string streamId,
//         StreamUploadState state,
//         ConcurrentDictionary<string, StreamUploadState> states) : IAsyncDisposable
//     {
//         public ValueTask DisposeAsync()
//         {
//             state.StateLock.Release();
//             state.Cts.Dispose();
//             states.TryRemove(streamId, out _);
//             return ValueTask.CompletedTask;
//         }
//     }
// }


public sealed class StreamJobCoordinator
{
    private sealed class JobState
    {
        public CancellationTokenSource Cts { get; } = new();
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly ConcurrentDictionary<string, JobState> _uploads = new();

    public CancellationToken RegisterUpload(string streamId)
    {
        var state = _uploads.GetOrAdd(streamId, _ => new JobState());
        return state.Cts.Token;
    }

    public void CompleteUpload(string streamId)
    {
        if (_uploads.TryRemove(streamId, out var state))
        {
            state.Completion.TrySetResult(true);
            state.Cts.Dispose();
        }
    }

    public async Task CancelUploadAndWaitAsync(string streamId, TimeSpan timeout)
    {
        if (!_uploads.TryGetValue(streamId, out var state))
            return; // nothing currently uploading

        state.Cts.Cancel();

        var completed = await Task.WhenAny(state.Completion.Task, Task.Delay(timeout));
        if (completed != state.Completion.Task)
        {
            // upload didn't stop in time - proceed with delete anyway, but log it
        }
    }
}