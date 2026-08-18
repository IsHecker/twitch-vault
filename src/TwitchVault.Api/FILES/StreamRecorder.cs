// // What's left here: start, stop, dispose, toggle-deletion. That's it.
// // No polling, no downloading, no retry counters, no URL building, no
// // chapter logic. If you can't describe what a method here does without
// // using the word "and," that's the test this class now passes.

// using TwitchVault.Api.Recording;

// public sealed class StreamRecorder : IStreamRecorder
// {
//     private readonly IRecordingLoop _recordingLoop;
//     private readonly IStreamFinalizer _finalizer;
//     private readonly ISegmentStore _segmentStore;
//     private readonly IStreamRepository _streamRepository;
//     private readonly ChapterTracker _chapterTracker;
//     private readonly IHlsPlaylist _hlsPlaylist;
//     private readonly ILogger<StreamRecorder> _logger;
//     private readonly CancellationTokenSource _cts;

//     private Domain.Stream _stream = null!;
//     private Channel _channel = null!;
//     private SessionEndReason? _requestedStopReason;

//     public StreamRecorder(
//         IRecordingLoop recordingLoop,
//         IStreamFinalizer finalizer,
//         ISegmentStore segmentStore,
//         IStreamRepository streamRepository,
//         ChapterTracker chapterTracker,
//         IHlsPlaylist hlsPlaylist,
//         ILogger<StreamRecorder> logger,
//         CancellationToken parentCancellationToken)
//     {
//         _recordingLoop = recordingLoop;
//         _finalizer = finalizer;
//         _segmentStore = segmentStore;
//         _streamRepository = streamRepository;
//         _chapterTracker = chapterTracker;
//         _hlsPlaylist = hlsPlaylist;
//         _logger = logger;
//         _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
//     }

//     public async Task StartAsync(Domain.Stream stream, Channel channel)
//     {
//         _stream = stream;
//         _channel = channel;
//         _chapterTracker.Attach(stream, channel);

//         _logger.LogInformation("Recording started for '{Channel}'", channel.Name);

//         SessionEndReason endReason;
//         try
//         {
//             endReason = await _recordingLoop.RunAsync(stream, channel, _cts.Token);
//         }
//         catch (OperationCanceledException) when (_cts.IsCancellationRequested)
//         {
//             // Explicit: cancellation means either the caller asked us to
//             // stop (StopAsync set a reason) or the parent token fired.
//             // No more Interlocked.CompareExchange guessing games.
//             endReason = _requestedStopReason ?? new SessionEndReason.StreamStopped();
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Unhandled error in recording session.");
//             endReason = new SessionEndReason.StreamError(ex);
//         }
//         finally
//         {
//             await DisposeAsync();
//             await _finalizer.FinalizeAsync(_stream, _channel, _segmentStore, endReason);
//         }
//     }

//     public async Task StopAsync(SessionEndReason? reason = null)
//     {
//         _requestedStopReason = reason ?? new SessionEndReason.StreamStopped();
//         await _cts.CancelAsync();
//     }

//     public async Task ToggleStreamDeletionAsync(bool markForDeletion)
//     {
//         _stream.MarkForDeletion = markForDeletion;
//         await _streamRepository.UpdateAsync(_stream);
//     }

//     public async ValueTask DisposeAsync()
//     {
//         if (!_cts.IsCancellationRequested)
//             await _cts.CancelAsync();

//         await _hlsPlaylist.DisposeAsync();
//         _chapterTracker.Dispose();
//         _cts.Dispose();
//     }
// }
