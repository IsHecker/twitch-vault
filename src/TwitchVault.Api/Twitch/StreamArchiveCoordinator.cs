// using System.Collections.Concurrent;

// namespace TwitchVault.Api.Twitch;

// public sealed class StreamUploadState
// {
//     public CancellationTokenSource Cts { get; } = new();
//     public SemaphoreSlim StateLock { get; } = new(1, 1);
// }

// public sealed class StreamArchiveCoordinator
// {
//     private readonly ConcurrentDictionary<string, StreamUploadState> _states = [];

//     public StreamUploadState GetUploadState(string streamId)
//         => _states.GetOrAdd(streamId, _ => new StreamUploadState());






//     public async Task UploadSegmentAsync(string streamId, StorageFile file, CancellationToken callerCt)
//     {
//         var state = _states.GetOrAdd(streamId, _ => new StreamUploadState());

//         if (Volatile.Read(ref state.MarkedForDeletion))
//             return; // don't even start new work once deletion is requested

//         using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerCt, state.Cts.Token);
//         var step = UploadOneSegmentAsync(streamId, file, state, linked.Token);
//         state.CurrentStep = step; // delete's handle to "when has this specific call unwound"

//         try { await step; }
//         catch (OperationCanceledException) when (state.Cts.IsCancellationRequested)
//         {
//             _logger.LogDebug("Upload for stream {StreamId} aborted for deletion.", streamId);
//         }
//     }

//     private async Task UploadOneSegmentAsync(
//         string streamId, StorageFile file, StreamUploadState state, CancellationToken ct)
//     {
//         var result = await _cloudStorage.UploadAsync([file], cancellationToken: ct);

//         // IMPORTANT: no ct here. Once UploadAsync returns successfully,
//         // the file exists on the cloud provider whether we like it or not.
//         // A cancellation that fires in this exact instant must NOT stop us
//         // from recording that fact — otherwise you get an upload that
//         // succeeded on the provider's side but was never written to your
//         // DB: an orphan, silently leaking storage forever. Committing a
//         // completed side effect is not optional just because a cancel
//         // request arrived a moment later; only *starting new* work should
//         // respect cancellation.
//         await state.StateLock.WaitAsync(CancellationToken.None);
//         try
//         {
//             var stream = await _streamRepository.GetByIdAsync(streamId);
//             // stream.AddRemoteSegment(result.Value.Url, result.Value.InstanceName);
//             await _streamRepository.UpdateAsync(stream);
//         }
//         finally { state.StateLock.Release(); }

//         File.Delete(file.FileName); // safe now: cloud copy is durable AND recorded
//     }

//     public async Task DeleteStreamAsync(string streamId, CancellationToken ct)
//     {
//         var state = _states.GetOrAdd(streamId, _ => new StreamUploadState());
//         Volatile.Write(ref state.MarkedForDeletion, true);

//         await state.Cts.CancelAsync(); // interrupt the in-flight HTTP call NOW

//         // Wait only for the ONE currently-running upload step to unwind —
//         // not the rest of the stream's backlog. If a call was mid-flight,
//         // this resolves in however long cancellation takes to propagate
//         // through the HTTP client, not "until the stream finishes."
//         try { await state.CurrentStep; } catch { /* already logged */ }

//         // Safe to read now: CurrentStep (including its checkpoint, if the
//         // upload had already succeeded) has fully completed before this
//         // line runs. Whatever's in RemoteSegments here is ground truth —
//         // including a segment that finished uploading right as we cancelled.
//         var stream = await _streamRepository.GetByIdAsync(streamId);
//         // foreach (var group in stream.RemoteSegments.GroupBy(s => s.InstanceName))
//         //     await _cloudStorage.DeleteBatchAsync(group.Key, group.Select(s => s.Url).ToList(), ct);

//         Directory.Delete(stream.Folder.RelativePath, recursive: true);
//         await _streamRepository.DeleteAsync(streamId);
//         _states.TryRemove(streamId, out _);
//     }
// }