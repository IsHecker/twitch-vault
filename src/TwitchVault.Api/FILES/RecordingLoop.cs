// // This is what used to be the 70-line body of RecordStreamAsync inside
// // StreamRecorder. It's now its own thing because it has its own
// // responsibility: "run one recording session's poll/fetch/store/playlist
// // cycle until it ends." StreamRecorder no longer needs to know HOW that
// // happens, just that it does, and what the result was.

// using TwitchVault.Api.Recording;

// public interface IRecordingLoop
// {
//     Task<SessionEndReason> RunAsync(Domain.Stream stream, Channel channel, CancellationToken cancellationToken);
// }

// public sealed class RecordingLoop : IRecordingLoop
// {
//     private readonly IThumbnailManager _thumbnailManager;
//     private readonly IManifestPoller _manifestPoller;
//     private readonly ISegmentFetcher _segmentFetcher;
//     private readonly ISegmentStore _segmentStore;
//     private readonly IHlsPlaylist _hlsPlaylist;
//     private readonly ISegmentUrlFormatter _urlFormatter;
//     private readonly IRetryPolicy _retryPolicy;
//     private readonly AppSettings _settings;
//     private readonly ILogger<RecordingLoop> _logger;

//     public RecordingLoop(
//         IThumbnailManager thumbnailManager,
//         IManifestPoller manifestPoller,
//         ISegmentFetcher segmentFetcher,
//         ISegmentStore segmentStore,
//         IHlsPlaylist hlsPlaylist,
//         ISegmentUrlFormatter urlFormatter,
//         IRetryPolicy retryPolicy,
//         SettingsService settingsService,
//         ILogger<RecordingLoop> logger)
//     {
//         _thumbnailManager = thumbnailManager;
//         _manifestPoller = manifestPoller;
//         _segmentFetcher = segmentFetcher;
//         _segmentStore = segmentStore;
//         _hlsPlaylist = hlsPlaylist;
//         _urlFormatter = urlFormatter;
//         _retryPolicy = retryPolicy;
//         _settings = settingsService.Settings;
//         _logger = logger;
//     }

//     public async Task<SessionEndReason> RunAsync(Domain.Stream stream, Channel channel, CancellationToken cancellationToken)
//     {
//         var emptyPollsRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;

//         while (!cancellationToken.IsCancellationRequested && emptyPollsRemaining > 0)
//         {
//             await _thumbnailManager.TryCaptureSnapshotAsync(channel.Name, stream);

//             var (manifest, hasQualityChanged) = await _retryPolicy.ExecuteAsync(
//                 () => _manifestPoller.GetNextManifestAsync(channel.Name, cancellationToken),
//                 cancellationToken);

//             if (string.IsNullOrWhiteSpace(manifest))
//             {
//                 emptyPollsRemaining--;
//                 _logger.LogWarning("No manifest available. {Remaining} attempts left.", emptyPollsRemaining);
//                 await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
//                 continue;
//             }

//             emptyPollsRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;

//             if (hasQualityChanged)
//                 await HandleQualitySwitchAsync(cancellationToken);

//             var manifestResult = ManifestSegmentExtractor.ExtractNewSegments(manifest, _hlsPlaylist.LastTwitchMediaSequence);
//             _hlsPlaylist.UpdateTwitchMediaSequence(manifestResult.LastMediaSequence);

//             await DownloadAndPersistSegmentsAsync(stream, manifestResult, cancellationToken);

//             if (manifestResult.IsStreamEnded)
//                 return new SessionEndReason.StreamEnded();

//             await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
//         }

//         cancellationToken.ThrowIfCancellationRequested();

//         // Only reachable via exhausting emptyPollsRemaining, not cancellation.
//         return new SessionEndReason.StreamEnded();
//     }

//     private async Task HandleQualitySwitchAsync(CancellationToken cancellationToken)
//     {
//         var closed = _segmentStore.CloseCurrentSegment();
//         await _hlsPlaylist.AddSegmentAsync(closed.FileName, closed.Duration, cancellationToken);
//         await _hlsPlaylist.AddDiscontinuityAsync(cancellationToken);
//         _logger.LogDebug("Quality switch detected.");
//     }

//     private async Task DownloadAndPersistSegmentsAsync(
//         Domain.Stream stream,
//         ManifestExtractionResult manifestResult,
//         CancellationToken cancellationToken)
//     {
//         // Sequential on purpose: HLS segment order in the output playlist
//         // must match arrival order. Parallelizing fetch would need a
//         // bounded, order-preserving buffer — worth it later if throughput
//         // becomes the bottleneck, not now.
//         foreach (var remoteSegment in manifestResult.Segments)
//         {
//             var fetched = await _retryPolicy.ExecuteAsync(
//                 () => _segmentFetcher.FetchAsync(remoteSegment, cancellationToken),
//                 cancellationToken);

//             var stored = await _segmentStore.SaveAsync(stream.Folder.RelativePath, fetched, cancellationToken);
//             var url = _urlFormatter.Format(stream.TwitchStreamId, stored.FileName);

//             if (remoteSegment.IsInitSegment && !_hlsPlaylist.HasInitSegment)
//                 await _hlsPlaylist.SetInitSegmentAsync(url, cancellationToken);
//             else
//                 await _hlsPlaylist.AddSegmentAsync(url, stored.Duration, cancellationToken);
//         }
//     }
// }