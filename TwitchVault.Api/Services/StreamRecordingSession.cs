using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public class StreamRecordingSession(
    Models.Stream stream,
    Channel channel,
    SegmentDownloader segmentDownloader,
    StreamRepository streamRepository,
    ChannelRepository channelRepository,
    TwitchClient twitchClient,
    AppSettings settings,
    PathsOptions pathsOptions,
    ILogger<StreamRecordingSession> logger,
    CancellationToken parentCancellationToken) : IAsyncDisposable
{
    private const int MaxPlaylistVariantsPolls = 10;
    private const int LeastPlaylistVariantsCount = 5;


    public Channel Channel => channel;

    private CancellationTokenSource _cts = null!;

    private PlaylistBuilder _playlistBuilder = null!;
    private MediaPlaylist[] _playlistVariants = [];
    private bool _allVariantsFetched;
    private int _stableVariantsCount;
    private int _masterPlaylistPolls;
    private DateTime _lastMasterPlaylistRefresh;
    private DateTime _sessionStartTime;
    private DateTime _lastSnapshotTime;

    private string _localThumbnailPath = null!;

    public async Task StartAsync()
    {
        await InitializeAsync(parentCancellationToken);
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

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stream.FolderPath);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _localThumbnailPath = Path.Combine(stream.FolderPath, "thumbnail.jpg");

        _playlistBuilder = await PlaylistBuilder.LoadOrCreateAsync(
            stream.FolderPath,
            settings.Vault.PlaylistFlushIntervalInSec,
            _cts.Token);

        _sessionStartTime = DateTime.Now;
        _lastSnapshotTime = DateTime.MinValue;
        _lastMasterPlaylistRefresh = DateTime.MinValue;

        stream.ThumbnailUrl = $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{channel.Name}-1280x720.jpg";
        await streamRepository.UpdateAsync(stream);
    }

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            var retriesRemaining = settings.Vault.MaxConsecutiveEmptyPolls;
            int currentQualityRank = -1;
            string? currentVariantUrl = null;

            while (!cancellationToken.IsCancellationRequested && retriesRemaining > 0)
            {
                await CaptureLiveThumbnailAsync();

                _playlistVariants = await GetPlaylistVariantsAsync(cancellationToken);
                if (_playlistVariants.Length == 0)
                {
                    retriesRemaining--;
                    logger.LogWarning("No playlist variants found for stream {StreamId}. Retries remaining: {Retries}",
                        stream.TwitchStreamId, retriesRemaining);

                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                var newQualityRank = await GetQualityRankAsync();
                var newVariantUrl = _playlistVariants[newQualityRank].Url;

                if (currentQualityRank != newQualityRank || currentVariantUrl != newVariantUrl)
                {
                    segmentDownloader.FlushCurrentSegment(_playlistBuilder);
                    _playlistBuilder.AddDiscontinuity();

                    logger.LogInformation("Polling playlist for stream {StreamId} using quality rank {Rank} (Bandwidth: {Bandwidth}) URL: {Url}",
                        stream.TwitchStreamId, newQualityRank + 1, _playlistVariants[newQualityRank].Bandwidth, newVariantUrl);
                }

                currentQualityRank = newQualityRank;
                currentVariantUrl = newVariantUrl;
                var selectedVariant = _playlistVariants[currentQualityRank];

                var playlistContent = await twitchClient.GetPlaylistContentAsync(selectedVariant.Url, cancellationToken);

                var pollInterval = ResolvePlaylistPollingInterval(playlistContent);

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
                    _playlistBuilder,
                    cancellationToken);

                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error in recording session for stream {StreamId}.", stream.TwitchStreamId);
        }
        finally
        {
            await FinalizeAsync();
        }
    }

    private async Task<MediaPlaylist[]> GetPlaylistVariantsAsync(CancellationToken cancellationToken)
    {
        if (_allVariantsFetched)
            return _playlistVariants;

        if (DateTime.Now - _lastMasterPlaylistRefresh < TimeSpan.FromSeconds(30))
            return _playlistVariants;

        _lastMasterPlaylistRefresh = DateTime.Now;

        var masterPlaylist = await twitchClient.GetMasterPlaylistAsync(channel.Name, cancellationToken);
        if (string.IsNullOrWhiteSpace(masterPlaylist))
            return _playlistVariants ?? [];

        var variants = MasterPlaylistParser.ParseVariants(masterPlaylist);
        if (variants.Length == 0)
            return _playlistVariants ?? [];

        if (variants.Length > _playlistVariants.Length)
        {
            _stableVariantsCount = 0;
        }
        else
        {
            _stableVariantsCount++;
        }

        _playlistVariants = variants;
        _masterPlaylistPolls++;

        if (_masterPlaylistPolls < 5 || (_playlistVariants.Length < LeastPlaylistVariantsCount && _stableVariantsCount < MaxPlaylistVariantsPolls))
            return _playlistVariants;

        _allVariantsFetched = true;
        logger.LogWarning("Master playlist variants stabilized for {Channel}. Total variants: {Count}", channel.Name, _playlistVariants.Length);

        for (int i = 0; i < _playlistVariants.Length; i++)
        {
            var v = _playlistVariants[i];
            logger.LogInformation("Variant Rank {Rank}: {Mbps}", i + 1, v.Bandwidth);
        }

        return _playlistVariants;
    }

    private static TimeSpan ResolvePlaylistPollingInterval(string? manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest))
            return TimeSpan.FromSeconds(2);

        var targetDurationStr = HlsTagReader.ReadTagValue(manifest, "TARGETDURATION");
        if (int.TryParse(targetDurationStr, out var targetDuration))
            return TimeSpan.FromSeconds(Math.Max(1, targetDuration / 2));

        return TimeSpan.FromSeconds(2);
    }

    private async Task TrySaveVODThumbnailAsync()
    {
        try
        {
            var thumbnailUrl = await twitchClient.GetVODThumbnailUrlAsync(channel.Name, CancellationToken.None);
            if (string.IsNullOrEmpty(thumbnailUrl))
                return;

            await SaveThumbnailAsync(thumbnailUrl);
            logger.LogInformation("Updated thumbnail for stream {StreamId} to VOD thumbnail: {Url}", stream.TwitchStreamId, thumbnailUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch/download VOD thumbnail for stream {StreamId}. Falling back to snapshot.", stream.TwitchStreamId);
        }
    }

    private async Task CaptureLiveThumbnailAsync()
    {
        try
        {
            var now = DateTime.Now;
            if (now - _sessionStartTime >= TimeSpan.FromMinutes(30) || now - _lastSnapshotTime < TimeSpan.FromMinutes(5))
                return;

            await SaveThumbnailAsync(stream.ThumbnailUrl);

            _lastSnapshotTime = now;
            logger.LogInformation("Captured live thumbnail snapshot for stream {StreamId}", stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture live thumbnail snapshot for stream {StreamId}", stream.TwitchStreamId);
        }
    }

    private async Task SaveThumbnailAsync(string imageUrl)
    {
        using var imageStream = await twitchClient.DownloadAsStreamAsync(imageUrl, CancellationToken.None);
        using var fileStream = File.Create(_localThumbnailPath);
        await imageStream.CopyToAsync(fileStream, CancellationToken.None);
    }

    private async Task FinalizeAsync()
    {
        try
        {
            await channelRepository.SetLiveAsync(channel.ChannelId, false);
            if (File.Exists(_localThumbnailPath))
                stream.ThumbnailUrl = new Uri(pathsOptions.BaseUrl + $"/{_localThumbnailPath}").AbsoluteUri;

            if (_playlistBuilder is not null)
                await _playlistBuilder.FinalizeAsync();

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

            // Confirm whether the stream truly ended or was just interrupted
            var liveInfo = await twitchClient.GetStreamMetadataAsync(channel.Name, CancellationToken.None);
            if (liveInfo?.TwitchStreamId == stream.TwitchStreamId)
            {
                stream.Status = StreamStatus.Interrupted;
                await streamRepository.UpdateAsync(stream);
                logger.LogWarning("Stream is Interrupted");

                return;
            }

            stream.MarkAsFinished();
            await TrySaveVODThumbnailAsync();
            await streamRepository.UpdateAsync(stream);

            logger.LogInformation("Stream Finished");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during finalization of stream {StreamId}.", stream.TwitchStreamId);
        }
    }

    private async Task DeleteStreamAsync()
    {
        try
        {
            if (Directory.Exists(stream.FolderPath))
                Directory.Delete(stream.FolderPath, recursive: true);

            await streamRepository.DeleteAsync(stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete stream {StreamId}.", stream.TwitchStreamId);
        }
    }

    private async Task<int> GetQualityRankAsync()
    {
        var rank = (await channelRepository.GetAllAsync())
            .First(c => c.Name == channel.Name).QualityRank - 1;

        var clampedRank = Math.Clamp(rank, 0, _playlistVariants.Length - 1);

        if (rank != clampedRank)
        {
            logger.LogWarning("Requested quality rank {Requested} but only {Count} variants available. Clamping to {Clamped}.", rank + 1, _playlistVariants.Length, clampedRank + 1);
        }

        return clampedRank;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync();

        if (_playlistBuilder is not null)
            await _playlistBuilder.DisposeAsync();

        _cts.Dispose();
    }
}