using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorder : IAsyncDisposable
{
    Task StartAsync(Domain.Stream stream, Channel channel);
    Task StopAsync();
    Task FinishAsync();
}

public sealed class StreamRecorder(
    IThumbnailManager thumbnailManager,
    IManifestPoller manifestPoller,
    ITwitchGqlClient twitchGqlClient,
    IHlsPlaylistWriter playlistWriter,
    ISegmentStore segmentStore,
    IChapterTracker chapterTracker,
    ISegmentUploader segmentUploader,
    IStreamFinalizer finalizer,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<StreamRecorder> logger,
    CancellationToken parentCancellationToken) : IStreamRecorder
{
    private const int EmptyPollsDelay = 2;

    private Domain.Stream _stream = null!;
    private Channel _channel = null!;
    private long _streamSizeBytes = 0;
    private readonly CancellationTokenSource _cts
        = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
    private SessionEndReason _endReason = new SessionEndReason.StreamEnded();

    private VaultOptions VaultOptions => vaultOptions.CurrentValue;

    public async Task StartAsync(Domain.Stream stream, Channel channel)
    {
        logger.LogInformation("Recording started for '{Channel}'", channel.Name);
        try
        {
            _channel = channel;
            _stream = stream;
            chapterTracker.Attach(_stream, _channel);
            segmentUploader.Attach(_stream);

            await RecordStreamAsync(_cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error in recording session.");
            SetEndReason(new SessionEndReason.StreamError(ex));
        }
        finally
        {
            await CloseCurrentSegmentAsync(CancellationToken.None);
            await segmentUploader.FlushRemainingAsync();
            await DisposeAsync();
            await finalizer.FinalizeAsync(_channel, _stream, _streamSizeBytes, _endReason);
        }
    }

    public async Task StopAsync()
    {
        SetEndReason(new SessionEndReason.StreamStopped());
        await _cts.CancelAsync();
    }

    public async Task FinishAsync()
    {
        SetEndReason(new SessionEndReason.StreamEnded());
        await _cts.CancelAsync();
    }

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        var emptyPollsRemaining = VaultOptions.MaxConsecutiveEmptyPolls;

        while (!cancellationToken.IsCancellationRequested && emptyPollsRemaining > 0)
        {
            await thumbnailManager.TryCaptureSnapshotAsync(_channel.Name, _stream, cancellationToken);

            var (manifest, hasQualityChanged) = await manifestPoller.GetNextManifestAsync(_channel, cancellationToken);

            if (string.IsNullOrWhiteSpace(manifest))
            {
                logger.LogWarning("No manifest available. {Remaining} attempts left.", emptyPollsRemaining--);
                await Task.Delay(TimeSpan.FromSeconds(EmptyPollsDelay), cancellationToken);
                continue;
            }

            emptyPollsRemaining = VaultOptions.MaxConsecutiveEmptyPolls;

            if (hasQualityChanged)
                await HandleQualitySwitchAsync(cancellationToken);

            var manifestResult = PlaylistSegmentExtractor.ExtractNewSegments(manifest, playlistWriter.LastTwitchMediaSequence);
            playlistWriter.UpdateTwitchMediaSequence(manifestResult.LastMediaSequence);

            var fetchedSegments = FetchSegmentsAsync(manifestResult, cancellationToken);
            await StoreSegmentsAsync(fetchedSegments, cancellationToken);

            if (manifestResult.IsStreamEnded)
            {
                if (manifestResult.Segments.Where(s => !s.IsInitSegment).Any())
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }

                SetEndReason(new SessionEndReason.StreamEnded());
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    private async Task HandleQualitySwitchAsync(CancellationToken cancellationToken)
    {
        await CloseCurrentSegmentAsync(cancellationToken);
        await playlistWriter.AddDiscontinuityAsync(cancellationToken);
        logger.LogDebug("Quality switch detected.");
    }

    private async IAsyncEnumerable<SegmentContent> FetchSegmentsAsync(
        PlaylistExtractionResult manifestResult,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var segment in manifestResult.Segments)
        {
            System.IO.Stream? segmentStream;
            try
            {
                segmentStream = await twitchGqlClient.DownloadAsStreamAsync(segment.Url, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound })
                    logger.LogInformation("Segment unavailable on CDN (404) for channel '{Channel}'. Skipping segment.", _channel.Name);
                else
                    logger.LogWarning(ex, "Failed to download a segment for channel '{Channel}'. Skipping segment.", _channel.Name);
                continue;
            }

            if (segmentStream is not null)
                yield return new SegmentContent(segment, segmentStream);
        }
    }

    private async Task StoreSegmentsAsync(
        IAsyncEnumerable<SegmentContent> fetchedSegments,
        CancellationToken cancellationToken)
    {
        await foreach (var segment in fetchedSegments)
        {
            try
            {
                if (segment.Source.IsInitSegment && playlistWriter.HasInitSegment)
                {
                    await segment.Content.DisposeAsync();
                    continue;
                }

                var localSegment = await segmentStore.SaveAsync(
                    _stream.Folder.GetAbsolutePath(Environment.CurrentDirectory),
                    segment,
                    playlistWriter.LastSegmentFileName,
                    cancellationToken);

                if (localSegment is null)
                    continue;

                _streamSizeBytes += localSegment.SizeBytes;

                if (segment.Source.IsInitSegment)
                    await playlistWriter.SetInitSegmentAsync(localSegment.FilePath, cancellationToken);
                else
                    await playlistWriter.AddSegmentAsync(localSegment.FilePath, localSegment.Duration, cancellationToken);

                await segmentUploader.AddAsync(localSegment);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error storing segment for channel '{Channel}'. Skipping segment.", _channel.Name);
            }
        }
    }

    private async Task CloseCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        var segment = segmentStore.CloseCurrentSegment();
        if (segment is null)
            return;

        _streamSizeBytes += segment.SizeBytes;
        await playlistWriter.AddSegmentAsync(segment.FilePath, segment.Duration, cancellationToken);
        await segmentUploader.AddAsync(segment);
    }

    private void SetEndReason(SessionEndReason reason) =>
        Interlocked.Exchange(ref _endReason, reason);

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync();

        await playlistWriter.DisposeAsync();
        segmentUploader.Dispose();
        chapterTracker.Dispose();
        _cts.Dispose();
    }
}