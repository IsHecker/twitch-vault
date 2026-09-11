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
    TimeProvider timeProvider,
    CancellationToken parentCancellationToken) : IStreamRecorder
{
    private static readonly TimeSpan EmptyPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StreamEndWaitInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private Domain.Stream _stream = null!;
    private Channel _channel = null!;
    private string _streamDirectory = string.Empty;
    private long _streamSizeBytes;
    private readonly List<SegmentContent> _segmentBuffer = [];

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
            _streamDirectory = _stream.Folder.GetAbsolutePath(Environment.CurrentDirectory);
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

            var (manifestStream, hasQualityChanged) = await manifestPoller.GetNextManifestAsync(_channel, cancellationToken);

            if (manifestStream == ResponseStream.Null)
            {
                logger.LogWarning("No manifest available. {Remaining} attempts left.", emptyPollsRemaining--);
                await Task.Delay(EmptyPollInterval, timeProvider, cancellationToken);
                continue;
            }

            emptyPollsRemaining = VaultOptions.MaxConsecutiveEmptyPolls;

            if (hasQualityChanged)
                await HandleQualitySwitchAsync(cancellationToken);

            var manifestResult = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
                manifestStream,
                playlistWriter.LastTwitchMediaSequence,
                cancellationToken);

            playlistWriter.UpdateTwitchMediaSequence(manifestResult.LastMediaSequence);

            var fetchedSegments = await FetchSegmentsAsync(manifestResult, cancellationToken);
            await StoreSegmentsAsync(fetchedSegments, cancellationToken);

            if (manifestResult.IsStreamEnded)
            {
                if (manifestResult.Segments.Where(s => !s.IsInitSegment).Any())
                {
                    await Task.Delay(StreamEndWaitInterval, timeProvider, cancellationToken);
                    continue;
                }

                SetEndReason(new SessionEndReason.StreamEnded());
                return;
            }

            await Task.Delay(PollInterval, timeProvider, cancellationToken);
        }
    }

    private async Task HandleQualitySwitchAsync(CancellationToken cancellationToken)
    {
        await CloseCurrentSegmentAsync(cancellationToken);
        await playlistWriter.AddDiscontinuityAsync(cancellationToken);
        logger.LogDebug("Quality switch detected.");
    }

    private async ValueTask<List<SegmentContent>> FetchSegmentsAsync(
        PlaylistExtractionResult manifestResult,
        CancellationToken cancellationToken)
    {
        _segmentBuffer.Clear();

        var segments = manifestResult.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            ResponseStream segmentStream;
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

            if (segmentStream != ResponseStream.Null)
                _segmentBuffer.Add(new SegmentContent(segment, segmentStream));
        }

        return _segmentBuffer;
    }

    private async Task StoreSegmentsAsync(
        List<SegmentContent> fetchedSegments,
        CancellationToken cancellationToken)
    {
        foreach (var segment in fetchedSegments)
        {
            try
            {
                if (segment.Source.IsInitSegment && playlistWriter.HasInitSegment)
                    continue;

                var localSegment = await segmentStore.SaveAsync(
                    _streamDirectory,
                    segment,
                    playlistWriter.LastSegmentFileName,
                    cancellationToken);

                if (!localSegment.HasValue)
                    continue;

                _streamSizeBytes += localSegment.Value.SizeBytes;

                if (segment.Source.IsInitSegment)
                    await playlistWriter.SetInitSegmentAsync(localSegment.Value.FilePath, cancellationToken);
                else
                    await playlistWriter.AddSegmentAsync(localSegment.Value.FilePath, localSegment.Value.Duration, cancellationToken);

                await segmentUploader.AddAsync(localSegment.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error storing segment for channel '{Channel}'. Skipping segment.", _channel.Name);
            }
            finally
            {
                await segment.ResponseStream.DisposeAsync();
            }
        }
    }

    private async Task CloseCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        var segment = segmentStore.CloseCurrentSegment();
        if (!segment.HasValue)
            return;

        _streamSizeBytes += segment.Value.SizeBytes;
        await playlistWriter.AddSegmentAsync(segment.Value.FilePath, segment.Value.Duration, cancellationToken);
        await segmentUploader.AddAsync(segment.Value);
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