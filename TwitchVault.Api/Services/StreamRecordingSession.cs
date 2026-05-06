using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class StreamRecordingSession(
    Models.Stream stream,
    StreamSegment streamSegment,
    Channel channel,
    SegmentDownloader segmentDownloader,
    StreamRepository streamRepository,
    StreamService streamService,
    ChannelRepository channelRepository,
    TwitchClient twitchClient,
    AppSettings settings,
    PathsOptions pathsOptions,
    ILoggerFactory loggerFactory,
    CancellationToken parentCancellationToken) : IAsyncDisposable
{
    public Channel Channel => channel;

    private CancellationTokenSource _cts = null!;
    private PlaylistBuilder _playlistBuilder = null!;
    private PlaylistVariantTracker _variantTracker = null!;
    private ThumbnailManager _thumbnailManager = null!;
    private ILogger<StreamRecordingSession> logger = null!;
    private bool _disposed;
    private bool _forcedFinish = false;
    private string? _finalizeReason;

    public async Task StartAsync()
    {
        await InitializeAsync();
        logger.LogInformation("Recording started: {Title} ({Category})",
            streamSegment.Title, streamSegment.CategoryName);
        await RecordStreamAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _finalizeReason = "Recording manually stopped.";
        streamSegment.MarkAsStopped();
        await streamRepository.UpdateSegmentAsync(streamSegment);
        await _cts.CancelAsync();
    }

    public async Task FinishAsync()
    {
        _finalizeReason = $"Segment '{streamSegment.SegmentNumber}' finished (Metadata split).";
        _forcedFinish = true;
        await _cts.CancelAsync();
    }

    public async Task ToggleStreamDeletion(bool markForDeletion)
    {
        streamSegment.MarkForDeletion = markForDeletion;
        await streamRepository.UpdateSegmentAsync(streamSegment);
    }


    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(streamSegment.FolderPath);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
        logger = loggerFactory.CreateLogger<StreamRecordingSession>();

        _playlistBuilder = await PlaylistBuilder.LoadOrCreateAsync(
            streamSegment.FolderPath,
            settings.Vault.PlaylistFlushIntervalInSec, _cts.Token);

        segmentDownloader.SetPlaylist(_playlistBuilder);

        _variantTracker = new PlaylistVariantTracker(
            channel.Name,
            twitchClient,
            loggerFactory.CreateLogger<PlaylistVariantTracker>());

        _thumbnailManager = new ThumbnailManager(
            stream,
            stream.StreamSegment,
            streamRepository,
            twitchClient,
            loggerFactory.CreateLogger<ThumbnailManager>());

        streamSegment.ThumbnailUrl = BuildLiveThumbnailUrl(channel.Name);
        await streamRepository.UpdateSegmentAsync(streamSegment);
    }

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            int consecutiveErrors = 0;
            int retriesRemaining = settings.Vault.MaxConsecutiveEmptyPolls;
            int lastQualityRank = -1;
            string? lastVariantUrl = null;

            while (!cancellationToken.IsCancellationRequested && retriesRemaining > 0)
            {
                try
                {
                    await _thumbnailManager.TryCaptureSnapshotAsync(channel.Name);

                    var playlistVariants = await _variantTracker.GetVariantsAsync(cancellationToken);
                    if (playlistVariants.Length == 0)
                    {
                        retriesRemaining--;
                        logger.LogWarning("Stream source unavailable. Retrying... ({Retries} left)", retriesRemaining);

                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        continue;
                    }

                    var (qualityRank, variantUrl) = await ResolveQualityAsync(playlistVariants);
                    if (lastQualityRank != qualityRank || lastVariantUrl != variantUrl)
                    {
                        segmentDownloader.FlushCurrentSegment();
                        _playlistBuilder.AddDiscontinuity();

                        (lastQualityRank, lastVariantUrl) = (qualityRank, variantUrl);

                        logger.LogInformation("Streaming quality: {Rank} ({Bandwidth} bps)",
                            qualityRank + 1, playlistVariants[qualityRank].Bandwidth);
                    }

                    var playlistContent = await twitchClient.GetPlaylistContentAsync(variantUrl, cancellationToken);
                    if (string.IsNullOrWhiteSpace(playlistContent))
                    {
                        retriesRemaining--;
                        logger.LogWarning("Playlist file not found. Retrying... ({Retries} left)", retriesRemaining);

                        var metadata = await twitchClient.GetStreamMetadataAsync(channel.Name, cancellationToken);
                        if (metadata.HasValue && metadata.Value.TwitchStreamId != stream.TwitchStreamId)
                            break;

                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                        continue;
                    }

                    retriesRemaining = settings.Vault.MaxConsecutiveEmptyPolls;
                    consecutiveErrors = 0;

                    var segments = SegmentParser.ParseNewSegments(playlistContent, _playlistBuilder);
                    await segmentDownloader.DownloadSegmentsAsync(
                        segments,
                        streamSegment.FolderPath,
                        settings.Vault.MaxSegmentDurationInSec,
                        cancellationToken);

                    if (playlistContent.AsSpan().Contains("#EXT-X-ENDLIST", StringComparison.Ordinal))
                    {
                        _forcedFinish = true;
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    consecutiveErrors++;
                    if (consecutiveErrors > 5)
                        throw;

                    logger.LogWarning("Network issue detected. Retry {Count}/5.", consecutiveErrors);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _finalizeReason = "Recording failed due to an error.";
            logger.LogError(ex, "Unhandled error in recording session.");
        }
        finally
        {
            await HandleStreamEndAsync();
        }
    }

    private async Task<(int rank, string url)> ResolveQualityAsync(MediaPlaylist[] variants)
    {
        var channels = await channelRepository.GetAllAsync();
        var requestedRank = channels.First(c => c.Name == channel.Name).QualityRank - 1;
        var clampedRank = Math.Clamp(requestedRank, 0, variants.Length - 1);

        if (requestedRank != clampedRank)
        {
            logger.LogWarning(
                "Requested quality rank {Requested} but only {Count} variants available. Clamped to {Clamped}.",
                requestedRank + 1, variants.Length, clampedRank + 1);
        }

        return (clampedRank, variants[clampedRank].Url);
    }

    private async Task HandleStreamEndAsync()
    {
        try
        {
            if (!_forcedFinish)
                await channelRepository.SetLiveAsync(channel.ChannelId, false);

            if (File.Exists(_thumbnailManager.LocalThumbnailPath))
                streamSegment.ThumbnailUrl = BuildLocalThumbnailUrl(_thumbnailManager.LocalThumbnailPath);

            if (_playlistBuilder is not null)
            {
                segmentDownloader.FlushCurrentSegment();
                await _playlistBuilder.FinalizeAsync();
            }

            await DisposeAsync();

            if (streamSegment.MarkForDeletion)
            {
                await DeleteStreamSegmentAsync();
                return;
            }

            if (streamSegment.Status == StreamStatus.Stopped)
            {
                await streamRepository.UpdateSegmentAsync(streamSegment);
                return;
            }

            if (await IsStreamStillLiveAsync())
            {
                streamSegment.Status = StreamStatus.Interrupted;
                await streamRepository.UpdateSegmentAsync(streamSegment);
                logger.LogWarning("Stream disconnected but still online on Twitch. Marking as interrupted.");
                return;
            }
            if (!_forcedFinish)
            {
                stream.StreamSegment = (await streamRepository.GetSegmentsByStreamIdAsync(stream.TwitchStreamId)).First();
                stream.FinishedAt = DateTime.Now;
            }

            streamSegment.MarkAsFinished();
            await _thumbnailManager.TrySaveVodThumbnailAsync(channel.Name);
            await streamRepository.UpdateStreamAsync(stream);
            await streamRepository.UpdateSegmentAsync(streamSegment);

            var duration = (stream.FinishedAt - stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
            logger.LogInformation("{Reason} Total duration: {Duration}", _finalizeReason ?? "Stream finished.", duration);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during finalization.");
        }
    }

    private async Task<bool> IsStreamStillLiveAsync()
    {
        var liveInfo = await twitchClient.GetStreamMetadataAsync(channel.Name, CancellationToken.None);
        return liveInfo?.TwitchStreamId == stream.TwitchStreamId && !_forcedFinish;
    }

    private async Task DeleteStreamSegmentAsync()
    {
        try
        {
            await streamService.DeleteSegmentAsync(stream.TwitchStreamId, streamSegment.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete segment {SegmentNumber} for stream {StreamId}.", streamSegment.SegmentNumber, stream.TwitchStreamId);
        }
    }

    private static string BuildLiveThumbnailUrl(string channelName) =>
        $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{channelName}-1280x720.jpg";

    private string BuildLocalThumbnailUrl(string localPath) =>
        new Uri(pathsOptions.BaseUrl + $"/{localPath}").AbsoluteUri;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync();

        if (_playlistBuilder is not null)
            await _playlistBuilder.DisposeAsync();

        _cts.Dispose();
    }
}