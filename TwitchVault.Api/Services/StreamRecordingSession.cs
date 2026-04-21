using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class StreamRecordingSession(
    Models.Stream stream,
    Channel channel,
    SegmentDownloader segmentDownloader,
    StreamRepository streamRepository,
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

    public async Task StartAsync()
    {
        await InitializeAsync();
        await RecordStreamAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        stream.MarkAsStopped();
        await streamRepository.UpdateAsync(stream);
        await _cts.CancelAsync();
    }

    public async Task ToggleStreamDeletion(bool markForDeletion)
    {
        stream.MarkForDeletion = markForDeletion;
        await streamRepository.UpdateAsync(stream);
    }


    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(stream.FolderPath);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
        logger = loggerFactory.CreateLogger<StreamRecordingSession>();

        _playlistBuilder = await PlaylistBuilder.LoadOrCreateAsync(
            stream.FolderPath,
            settings.Vault.PlaylistFlushIntervalInSec, _cts.Token);

        segmentDownloader.SetPlaylist(_playlistBuilder);

        _variantTracker = new PlaylistVariantTracker(channel.Name, twitchClient,
            loggerFactory.CreateLogger<PlaylistVariantTracker>());

        _thumbnailManager = new ThumbnailManager(stream, twitchClient,
           loggerFactory.CreateLogger<ThumbnailManager>());

        stream.ThumbnailUrl = BuildLiveThumbnailUrl(channel.Name);
        await streamRepository.UpdateAsync(stream);
    }

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            int retriesRemaining = settings.Vault.MaxConsecutiveEmptyPolls;
            int lastQualityRank = -1;
            string? lastVariantUrl = null;

            while (!cancellationToken.IsCancellationRequested && retriesRemaining > 0)
            {
                await _thumbnailManager.TryCaptureSnapshotAsync();

                var playlistVariants = await _variantTracker.GetVariantsAsync(cancellationToken);
                if (playlistVariants.Length == 0)
                {
                    retriesRemaining--;
                    logger.LogWarning("No playlist variants found for stream {StreamId}. Retries remaining: {Retries}",
                        stream.TwitchStreamId, retriesRemaining);

                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                var (qualityRank, variantUrl) = await ResolveQualityAsync(playlistVariants);
                if (lastQualityRank != qualityRank || lastVariantUrl != variantUrl)
                {
                    segmentDownloader.FlushCurrentSegment();
                    _playlistBuilder.AddDiscontinuity();

                    (lastQualityRank, lastVariantUrl) = (qualityRank, variantUrl);

                    logger.LogInformation("Polling playlist for stream {StreamId} using quality rank {Rank} (Bandwidth: {Bandwidth}) URL: {Url}",
                        stream.TwitchStreamId, qualityRank + 1, playlistVariants[qualityRank].Bandwidth, $"{variantUrl[..100]}...");
                }

                var playlistContent = await twitchClient.GetPlaylistContentAsync(variantUrl, cancellationToken);
                var pollInterval = ResolvePlaylistPollInterval(playlistContent);

                if (string.IsNullOrWhiteSpace(playlistContent))
                {
                    retriesRemaining--;
                    logger.LogWarning("Playlist not found for stream {StreamId}. Retries remaining: {Retries}",
                        stream.TwitchStreamId, retriesRemaining);

                    await Task.Delay(pollInterval, cancellationToken);
                    continue;
                }

                retriesRemaining = settings.Vault.MaxConsecutiveEmptyPolls;

                var segments = SegmentParser.ParseNewSegments(playlistContent, _playlistBuilder);
                await segmentDownloader.DownloadSegmentsAsync(
                    segments,
                    stream.FolderPath,
                    settings.Vault.MaxSegmentDurationInSec,
                    cancellationToken);

                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Unhandled error in recording session for stream {StreamId}.",
                stream.TwitchStreamId);
        }
        finally
        {
            await HandleStreamEndAsync();
        }
    }

    private static TimeSpan ResolvePlaylistPollInterval(string? manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest))
            return TimeSpan.FromSeconds(2);

        var targetDurationStr = HlsTagReader.ReadTagValue(manifest, "TARGETDURATION");
        if (int.TryParse(targetDurationStr, out var targetDuration))
            return TimeSpan.FromSeconds(Math.Max(1, targetDuration / 2));

        return TimeSpan.FromSeconds(2);
    }

    private async Task<(int rank, string url)> ResolveQualityAsync(MediaPlaylist[] variants)
    {
        var channels = await channelRepository.GetAllAsync();
        var requestedRank = channels.First(c => c.Name == channel.Name).QualityRank - 1;
        var clampedRank = Math.Clamp(requestedRank, 0, variants.Length - 1);

        if (requestedRank != clampedRank)
        {
            logger.LogWarning(
                "Requested quality rank {Requested} but only {Count} variants available. Clamping to {Clamped}.",
                requestedRank + 1, variants.Length, clampedRank + 1);
        }

        return (clampedRank, variants[clampedRank].Url);
    }

    private async Task HandleStreamEndAsync()
    {
        try
        {
            await channelRepository.SetLiveAsync(channel.ChannelId, false);

            if (File.Exists(_thumbnailManager.LocalThumbnailPath))
                stream.ThumbnailUrl = BuildLocalThumbnailUrl(_thumbnailManager.LocalThumbnailPath);

            if (_playlistBuilder is not null)
            {
                segmentDownloader.FlushCurrentSegment();
                await _playlistBuilder.FinalizeAsync();
            }

            await DisposeAsync();
            if (stream.MarkForDeletion)
            {
                await DeleteStreamAsync();
                return;
            }

            if (stream.Status == StreamStatus.Stopped)
            {
                await streamRepository.UpdateAsync(stream);
                return;
            }

            if (await IsStreamStillLiveAsync())
            {
                stream.Status = StreamStatus.Interrupted;
                await streamRepository.UpdateAsync(stream);
                logger.LogWarning("Stream {StreamId} is interrupted (still live on Twitch).", stream.TwitchStreamId);
                return;
            }

            stream.MarkAsFinished();
            await _thumbnailManager.TrySaveVodThumbnailAsync(channel.Name);
            await streamRepository.UpdateAsync(stream);

            logger.LogInformation("Stream {StreamId} finished.", stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during finalization of stream {StreamId}.",
                stream.TwitchStreamId);
        }
    }

    private async Task<bool> IsStreamStillLiveAsync()
    {
        var liveInfo = await twitchClient.GetStreamMetadataAsync(channel.Name, CancellationToken.None);
        return liveInfo?.TwitchStreamId == stream.TwitchStreamId;
    }

    private async Task DeleteStreamAsync()
    {
        try
        {
            await IOUtils.DeleteDirectoryWithRetriesAsync(stream.FolderPath);

            await streamRepository.DeleteAsync(stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete stream {StreamId}.", stream.TwitchStreamId);
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