using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class StreamRecordingSession : IAsyncDisposable
{
    private abstract record SessionEndReason;
    private record StreamEnded : SessionEndReason;
    private record StreamSplit : SessionEndReason;
    private record StreamStopped : SessionEndReason;
    private record StreamError(Exception Ex) : SessionEndReason;


    private readonly Models.Stream _stream;
    private readonly StreamSegment _streamSegment;
    private readonly Channel _channel;
    private readonly SegmentDownloader _segmentDownloader;
    private readonly StreamRepository _streamRepository;
    private readonly StreamService _streamService;
    private readonly ChannelRepository _channelRepository;
    private readonly TwitchClient _twitchClient;
    private readonly AppSettings _settings;
    private readonly PathsOptions _pathsOptions;
    private readonly ILogger<StreamRecordingSession> _logger;

    private readonly CancellationTokenSource _cts;
    private readonly PlaylistBuilder _playlistBuilder;
    private readonly PlaylistVariantTracker _variantTracker;
    private readonly ThumbnailManager _thumbnailManager;

    private SessionEndReason? _finalizeReason;
    private bool _disposed;

    public Channel Channel => _channel;

    // -------------------------------------------------------------------------
    // Construction — use the static factory so async init stays off the
    // constructor, but all fields are set exactly once and are readonly.
    // -------------------------------------------------------------------------

    private StreamRecordingSession(
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
        PlaylistBuilder playlistBuilder,
        ILoggerFactory loggerFactory,
        CancellationToken parentCancellationToken)
    {
        _stream = stream;
        _streamSegment = streamSegment;
        _channel = channel;
        _segmentDownloader = segmentDownloader;
        _streamRepository = streamRepository;
        _streamService = streamService;
        _channelRepository = channelRepository;
        _twitchClient = twitchClient;
        _settings = settings;
        _pathsOptions = pathsOptions;
        _playlistBuilder = playlistBuilder;
        _logger = loggerFactory.CreateLogger<StreamRecordingSession>();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);

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
    }

    public static async Task<StreamRecordingSession> CreateAsync(
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
        CancellationToken parentCancellationToken)
    {
        Directory.CreateDirectory(streamSegment.FolderPath);

        var playlistBuilder = await PlaylistBuilder.LoadOrCreateAsync(
            streamSegment.FolderPath,
            settings.Vault.PlaylistFlushIntervalInSec,
            parentCancellationToken);

        var session = new StreamRecordingSession(
            stream, streamSegment, channel,
            segmentDownloader, streamRepository, streamService,
            channelRepository, twitchClient, settings, pathsOptions,
            playlistBuilder, loggerFactory, parentCancellationToken);

        segmentDownloader.SetPlaylist(playlistBuilder);

        streamSegment.ThumbnailUrl = BuildLiveThumbnailUrl(channel.Name);
        await streamRepository.UpdateSegmentAsync(streamSegment);

        return session;
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Recording started: {Title} ({Category})",
            _streamSegment.Title, _streamSegment.CategoryName);

        try
        {
            await RecordStreamAsync(_cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetEndReason(new StreamError(ex));
            _logger.LogError(ex, "Unhandled error in recording session.");
        }
        finally
        {
            await FinalizeSessionAsync();
        }
    }

    public async Task StopAsync()
    {
        SetEndReason(new StreamStopped());
        await _cts.CancelAsync();
    }

    public async Task FinishAsync()
    {
        SetEndReason(new StreamSplit());
        await _cts.CancelAsync();
    }

    public async Task ToggleStreamDeletion(bool markForDeletion)
    {
        _streamSegment.MarkForDeletion = markForDeletion;
        await _streamRepository.UpdateSegmentAsync(_streamSegment);
    }


    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        int consecutiveNetworkErrors = 0;
        int retriesRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;
        int lastQualityRank = -1;
        string? lastVariantUrl = null;

        while (!cancellationToken.IsCancellationRequested && retriesRemaining > 0)
        {
            try
            {
                await _thumbnailManager.TryCaptureSnapshotAsync(_channel.Name);

                var variants = await _variantTracker.GetVariantsAsync(cancellationToken);
                if (variants.Length == 0)
                {
                    _logger.LogWarning("Stream source unavailable. Retrying… ({Retries} left)", --retriesRemaining);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                var (qualityRank, variantUrl) = await ResolveQualityAsync(variants);
                if (lastQualityRank != qualityRank || lastVariantUrl != variantUrl)
                {
                    _segmentDownloader.FlushCurrentSegment();
                    _playlistBuilder.AddDiscontinuity();

                    (lastQualityRank, lastVariantUrl) = (qualityRank, variantUrl);

                    _logger.LogInformation("Streaming quality: {Rank} ({Bandwidth} bps)",
                        qualityRank + 1, variants[qualityRank].Bandwidth);
                }

                var playlistContent = await _twitchClient.GetPlaylistContentAsync(variantUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(playlistContent))
                {
                    _logger.LogWarning("Playlist not found. Retrying... ({Retries} left)", --retriesRemaining);

                    var metadata = await _twitchClient.GetStreamMetadataAsync(_channel.Name, cancellationToken);
                    if (metadata.HasValue && metadata.Value.TwitchStreamId != _stream.TwitchStreamId)
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                retriesRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;
                consecutiveNetworkErrors = 0;

                var segments = SegmentParser.ParseNewSegments(playlistContent, _playlistBuilder);
                await _segmentDownloader.DownloadSegmentsAsync(
                    segments,
                    _streamSegment.FolderPath,
                    _settings.Vault.MaxSegmentDurationInSec,
                    cancellationToken);

                if (playlistContent.AsSpan().Contains("#EXT-X-ENDLIST", StringComparison.Ordinal))
                {
                    SetEndReason(new StreamEnded());
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                consecutiveNetworkErrors++;
                if (consecutiveNetworkErrors > 5) throw;

                _logger.LogWarning("Network issue detected. Retry {Count}/5.", consecutiveNetworkErrors);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async Task<(int rank, string url)> ResolveQualityAsync(MediaPlaylist[] variants)
    {
        var channels = await _channelRepository.GetAllAsync();
        var requestedRank = channels.First(c => c.Name == _channel.Name).QualityRank - 1;
        var clampedRank = Math.Clamp(requestedRank, 0, variants.Length - 1);

        if (requestedRank != clampedRank)
        {
            _logger.LogWarning(
                "Requested quality rank {Requested} but only {Count} variants available. Clamped to {Clamped}.",
                requestedRank + 1, variants.Length, clampedRank + 1);
        }

        return (clampedRank, variants[clampedRank].Url);
    }

    private async Task FinalizeSessionAsync()
    {
        try
        {
            FlushAndFinalizePlaylist();

            if (File.Exists(_thumbnailManager.LocalThumbnailPath))
                _streamSegment.ThumbnailUrl = BuildLocalThumbnailUrl(_thumbnailManager.LocalThumbnailPath);

            await _streamRepository.UpdateSegmentAsync(_streamSegment);
            await DisposeAsync();

            if (_streamSegment.MarkForDeletion)
            {
                await DeleteSegmentAsync();
                return;
            }

            switch (_finalizeReason)
            {
                case StreamStopped:
                    _streamSegment.MarkAsStopped();
                    await _streamRepository.UpdateSegmentAsync(_streamSegment);
                    _logger.LogInformation("Recording manually stopped.");
                    return;

                case StreamSplit:
                    _streamSegment.MarkAsFinished();
                    await _streamRepository.UpdateSegmentAsync(_streamSegment);

                    _logger.LogInformation("Segment '{SegmentNumber}' closed (metadata split).",
                        _streamSegment.SegmentNumber);
                    return;

                case StreamError(var ex):
                    _logger.LogError(ex, "Session ended due to an error.");

                    if (!await IsStreamStillLiveAsync())
                        return;

                    _streamSegment.Status = StreamStatus.Interrupted;
                    await _streamRepository.UpdateSegmentAsync(_streamSegment);
                    _logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
                    return;

                case StreamEnded:
                case null:
                    await HandleStreamEndedAsync();
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session finalization.");
        }
    }

    private async Task HandleStreamEndedAsync()
    {
        // if (await IsStreamStillLiveAsync())
        // {
        //     _streamSegment.Status = StreamStatus.Interrupted;
        //     await _streamRepository.UpdateSegmentAsync(_streamSegment);
        //     _logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
        //     return;
        // }

        _stream.StreamSegment = (await _streamRepository.GetSegmentsByStreamIdAsync(_stream.TwitchStreamId)).First();
        _stream.FinishedAt = DateTime.Now;

        _streamSegment.MarkAsFinished();

        await _thumbnailManager.TrySaveVodThumbnailAsync(_channel.Name);
        await _streamRepository.UpdateStreamAsync(_stream);
        await _streamRepository.UpdateSegmentAsync(_streamSegment);

        await _channelRepository.SetLiveAsync(_channel.ChannelId, false);

        var duration = (_stream.FinishedAt - _stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        _logger.LogInformation("Stream finished. Total duration: {Duration}", duration);
    }

    private void FlushAndFinalizePlaylist()
    {
        _segmentDownloader.FlushCurrentSegment();
        _ = _playlistBuilder.FinalizeAsync();
    }

    private async Task<bool> IsStreamStillLiveAsync()
    {
        var metadata = await _twitchClient.GetStreamMetadataAsync(_channel.Name, CancellationToken.None);
        return metadata?.TwitchStreamId == _stream.TwitchStreamId;
    }

    private async Task DeleteSegmentAsync()
    {
        try
        {
            await _streamService.DeleteSegmentAsync(_stream.TwitchStreamId, _streamSegment.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete segment {SegmentNumber} for stream {StreamId}.",
                _streamSegment.SegmentNumber, _stream.TwitchStreamId);
        }
    }

    private void SetEndReason(SessionEndReason reason) =>
        Interlocked.CompareExchange(ref _finalizeReason, reason, null);

    private static string BuildLiveThumbnailUrl(string channelName) =>
        $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{channelName}-1280x720.jpg";

    private string BuildLocalThumbnailUrl(string localPath) =>
        new Uri(_pathsOptions.BaseUrl + $"/{localPath}").AbsoluteUri;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync();

        await _playlistBuilder.DisposeAsync();
        _cts.Dispose();
    }
}