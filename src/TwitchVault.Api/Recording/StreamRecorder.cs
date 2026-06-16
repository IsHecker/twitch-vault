using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Recording;

public sealed class StreamRecorder : IAsyncDisposable
{
    private abstract record SessionEndReason;
    private record StreamEnded : SessionEndReason;
    private record StreamStopped : SessionEndReason;
    private record StreamError(Exception Ex) : SessionEndReason;

    private readonly Domain.Stream _stream;
    private readonly Channel _channel;
    private readonly SegmentDownloader _segmentDownloader;
    private readonly IStreamRepository _streamRepository;
    private readonly StreamService _streamService;
    private readonly ChannelRepository _channelRepository;
    private readonly ITwitchGqlClient _twitchClient;
    private readonly AppSettings _settings;
    private readonly PathsOptions _pathsOptions;
    private readonly EventBus _eventBus;
    private readonly ILogger<StreamRecorder> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly HlsPlaylist _playlistBuilder;
    private readonly PlaylistVariantTracker _variantTracker;
    private readonly ThumbnailManager _thumbnailManager;
    private readonly IDateTimeProvider _dateTimeProvider;
    private SessionEndReason? _finalizeReason;
    private bool _disposed;

    private StreamRecorder(
        Domain.Stream stream,
        Channel channel,
        SegmentDownloader segmentDownloader,
        HlsPlaylist playlistBuilder,
        IStreamRepository streamRepository,
        StreamService streamService,
        ChannelRepository channelRepository,
        ITwitchGqlClient twitchGqlClient,
        AppSettings settings,
        PathsOptions pathsOptions,
        EventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken parentCancellationToken)
    {
        _stream = stream;
        _channel = channel;
        _segmentDownloader = segmentDownloader;
        _streamRepository = streamRepository;
        _streamService = streamService;
        _channelRepository = channelRepository;
        _twitchClient = twitchGqlClient;
        _settings = settings;
        _pathsOptions = pathsOptions;
        _playlistBuilder = playlistBuilder;
        _eventBus = eventBus;
        _dateTimeProvider = dateTimeProvider;
        _logger = loggerFactory.CreateLogger<StreamRecorder>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);

        _variantTracker = new PlaylistVariantTracker(
            channel.Name,
            twitchGqlClient,
            dateTimeProvider,
            loggerFactory.CreateLogger<PlaylistVariantTracker>());

        _thumbnailManager = new ThumbnailManager(
            stream,
            dateTimeProvider,
            twitchGqlClient,
            loggerFactory.CreateLogger<ThumbnailManager>());

        eventBus.Subscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
    }

    public static async Task<StreamRecorder> CreateAsync(
        Domain.Stream stream,
        Channel channel,
        SegmentDownloader segmentDownloader,
        IStreamRepository streamRepository,
        StreamService streamService,
        ChannelRepository channelRepository,
        ITwitchGqlClient twitchGqlClient,
        AppSettings settings,
        PathsOptions pathsOptions,
        EventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken parentCancellationToken)
    {
        Directory.CreateDirectory(stream.FolderPath);
        var playlistBuilder = await HlsPlaylist.LoadOrCreateAsync(
            stream.FolderPath,
            dateTimeProvider,
            settings.Vault.PlaylistFlushIntervalInSec);

        var session = new StreamRecorder(
            stream,
            channel,
            segmentDownloader,
            playlistBuilder,
            streamRepository,
            streamService,
            channelRepository,
            twitchGqlClient,
            settings,
            pathsOptions,
            eventBus,
            dateTimeProvider,
            loggerFactory,
            parentCancellationToken);

        segmentDownloader.SetPlaylist(playlistBuilder);
        stream.ThumbnailUrl = BuildLiveThumbnailUrl(channel.Name);
        await streamRepository.UpdateAsync(stream);
        return session;
    }

    private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
    {
        if (_channel.ChannelId != e.ChannelId)
            return;
        using var _chnlScope = _logger.BeginScope("{Channel}", _channel.Name);
        using var _metaScope = _logger.BeginScope("'{Title}' ({Category})", e.Title, e.CategoryName);

        if (_stream.CurrentChapter.Title == e.Title && _stream.CurrentChapter.Category == e.CategoryName)
        {
            _logger.LogInformation("Metadata change ignored: title and category unchanged.");
            return;
        }

        _logger.LogInformation("Metadata split triggered");
        _stream.AddChapter(e.Title, e.CategoryName, _dateTimeProvider.DateTimeNow);
        await _streamRepository.UpdateAsync(_stream);
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Recording started for '{Channel}'", _channel.Name);
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
            await FinalizeRecorderAsync();
        }
    }

    public async Task StopAsync()
    {
        SetEndReason(new StreamStopped());
        await _cts.CancelAsync();
    }

    public async Task ToggleStreamDeletion(bool markForDeletion)
    {
        _stream.MarkForDeletion = markForDeletion;
        await _streamRepository.UpdateAsync(_stream);
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
                await _thumbnailManager.TryCaptureSnapshotAsync();
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
                    _logger.LogDebug("Streaming quality: {Rank} ({Bandwidth} bps)",
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

                var playlistResult = SegmentParser.ParseNewSegments(playlistContent, _playlistBuilder.LastMediaSequence);

                await _segmentDownloader.DownloadSegmentsAsync(
                    playlistResult,
                    _stream.FolderPath,
                    _settings.Vault.MaxSegmentDurationInSec,
                    cancellationToken);

                _playlistBuilder.UpdateMediaSequence(playlistResult.LastMediaSequence);

                if (playlistResult.IsStreamEnded)
                {
                    SetEndReason(new StreamEnded());
                    return;
                }

                retriesRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;
                consecutiveNetworkErrors = 0;

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
            _logger.LogInformation(
                "Requested quality rank {Requested} but only {Count} variants available. Clamped to {Clamped}.",
                requestedRank + 1, variants.Length, clampedRank + 1);
        }

        return (clampedRank, variants[clampedRank].Url);
    }

    private async Task FinalizeRecorderAsync()
    {
        try
        {
            await FinalizePlaylistAsync();
            _stream.ThumbnailUrl = BuildLocalThumbnailUrl(_thumbnailManager.LocalThumbnailPath);
            await _streamRepository.UpdateAsync(_stream);
            await DisposeAsync();

            if (_stream.MarkForDeletion)
            {
                await _streamService.DeleteStreamAsync(_stream.TwitchStreamId);
                return;
            }

            switch (_finalizeReason)
            {
                case StreamStopped:
                    _stream.MarkAsStopped(_dateTimeProvider.DateTimeNow);
                    await _streamRepository.UpdateAsync(_stream);
                    _logger.LogDebug("Recording manually stopped.");
                    return;

                case StreamError(var ex):
                    _logger.LogError(ex, "Session ended due to an error.");
                    if (!await IsStreamStillLiveAsync())
                        return;

                    _stream.MarkAsInterrupted();
                    await _streamRepository.UpdateAsync(_stream);
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
        _stream.MarkAsFinished(_dateTimeProvider.DateTimeNow);
        await _streamRepository.UpdateAsync(_stream);
        await _channelRepository.SetLiveAsync(_channel.ChannelId, false);
        var duration = (_stream.FinishedAt - _stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        _logger.LogInformation("Stream finished. Total duration: {Duration}", duration);
    }

    private async Task FinalizePlaylistAsync()
    {
        _segmentDownloader.FlushCurrentSegment();
        await _playlistBuilder.FinalizeAsync();
    }

    private async Task<bool> IsStreamStillLiveAsync()
    {
        var metadata = await _twitchClient.GetStreamMetadataAsync(_channel.Name, CancellationToken.None);
        return metadata?.TwitchStreamId == _stream.TwitchStreamId;
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
        _eventBus.UnSubscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
        _cts.Dispose();
    }
}