using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorder : IAsyncDisposable
{
    Task StartAsync(Domain.Stream stream, Channel channel);
    Task StopAsync();
    Task ToggleStreamDeletionAsync(bool markForDeletion);
}

public sealed class StreamRecorder : IStreamRecorder, IAsyncDisposable
{
    private abstract record SessionEndReason;
    private record StreamEnded : SessionEndReason;
    private record StreamStopped : SessionEndReason;
    private record StreamError(Exception Ex) : SessionEndReason;

    private Domain.Stream _stream = null!;
    private Channel _channel = null!;
    private readonly SegmentDownloader _segmentDownloader;
    private readonly ThumbnailManager _thumbnailManager;
    private readonly HlsPlaylist _hlsPlaylist;
    private readonly IStreamRepository _streamRepository;
    private readonly IStreamService _streamService;
    private readonly IChannelRepository _channelRepository;
    private readonly ITwitchGqlClient _twitchClient;
    private readonly AppSettings _settings;
    private readonly PathsOptions _pathsOptions;
    private readonly EventBus _eventBus;
    private readonly ILogger<StreamRecorder> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly ManifestPoller _playlistVariantTracker;
    private readonly IDateTimeProvider _dateTimeProvider;
    private SessionEndReason? _finalizeReason;

    public StreamRecorder(
        SegmentDownloader segmentDownloader,
        ManifestPoller playlistVariantTracker,
        ThumbnailManager thumbnailManager,
        HlsPlaylist hlsPlaylist,
        IStreamRepository streamRepository,
        IStreamService streamService,
        IChannelRepository channelRepository,
        ITwitchGqlClient twitchGqlClient,
        SettingsService settingsService,
        IOptions<PathsOptions> pathsOptions,
        EventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        ILogger<StreamRecorder> logger,
        CancellationToken parentCancellationToken)
    {
        _segmentDownloader = segmentDownloader;
        _playlistVariantTracker = playlistVariantTracker;
        _thumbnailManager = thumbnailManager;
        _streamRepository = streamRepository;
        _streamService = streamService;
        _channelRepository = channelRepository;
        _twitchClient = twitchGqlClient;
        _settings = settingsService.Settings;
        _pathsOptions = pathsOptions.Value;
        _hlsPlaylist = hlsPlaylist;
        _eventBus = eventBus;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);

        eventBus.Subscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
    }

    private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
    {
        if (_channel.Id != e.ChannelId)
            return;

        using var _chnlScope = _logger.BeginScope("{Channel}", _channel.Name);
        using var _metaScope = _logger.BeginScope("'{Title}' ({Category})", e.Title, e.CategoryName);

        if (_stream.CurrentChapter.Title == e.Title && _stream.CurrentChapter.Category == e.CategoryName)
        {
            _logger.LogInformation("Metadata change ignored: title and category unchanged.");
            return;
        }

        _logger.LogInformation("Metadata split triggered.");
        _stream.AddChapter(e.Title, e.CategoryName, _dateTimeProvider.DateTimeNow);
        await _streamRepository.UpdateAsync(_stream);
    }

    public async Task StartAsync(Domain.Stream stream, Channel channel)
    {
        _logger.LogInformation("Recording started for '{Channel}'", channel.Name);
        try
        {
            _channel = channel;
            _stream = stream;
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

    public async Task ToggleStreamDeletionAsync(bool markForDeletion)
    {
        _stream.MarkForDeletion = markForDeletion;
        await _streamRepository.UpdateAsync(_stream);
    }

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        int consecutiveNetworkErrors = 0;
        int emptyPollsRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;

        while (!cancellationToken.IsCancellationRequested && emptyPollsRemaining > 0)
        {
            try
            {
                await _thumbnailManager.TryCaptureSnapshotAsync(_stream);

                var (manifest, hasQualityChanged) = await _playlistVariantTracker
                    .GetNextManifestAsync(_channel.Name, cancellationToken);

                if (manifest is null)
                {
                    _logger.LogWarning("No manifest available. Retrying... ({Remaining} attempts left)",
                    emptyPollsRemaining--);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                if (hasQualityChanged)
                {
                    var (FileName, Duration) = _segmentDownloader.CloseSegment();
                    await _hlsPlaylist.AddSegmentAsync(FileName, Duration, cancellationToken);
                    await _hlsPlaylist.AddDiscontinuityAsync(cancellationToken);
                    _logger.LogDebug("Quality switch detected.");
                }


                var manifestResult = ManifestSegmentExtractor.ExtractNewSegments(manifest, _hlsPlaylist.LastTwitchMediaSequence);
                _hlsPlaylist.UpdateTwitchMediaSequence(manifestResult.LastMediaSequence);

                var downloadedSegments = _segmentDownloader.DownloadSegmentsAsync(
                    _stream.Folder.RelativePath,
                    manifestResult,
                    _hlsPlaylist,
                    cancellationToken);

                await foreach (var (FileName, Duration) in downloadedSegments)
                {
                    if (!string.IsNullOrWhiteSpace(manifestResult.InitSegmentUrl) && !_hlsPlaylist.HasInitSegment)
                        await _hlsPlaylist.SetInitSegmentAsync(FileName, cancellationToken);
                    else
                        await _hlsPlaylist.AddSegmentAsync(FileName, Duration, cancellationToken);
                }

                if (manifestResult.IsStreamEnded)
                {
                    SetEndReason(new StreamEnded());
                    return;
                }

                emptyPollsRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;
                consecutiveNetworkErrors = 0;

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                consecutiveNetworkErrors++;
                if (consecutiveNetworkErrors > 5)
                    throw;

                _logger.LogWarning("Network issue detected. Retry {Count}/5.", consecutiveNetworkErrors);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async Task FinalizeRecorderAsync()
    {
        try
        {
            await FinalizePlaylistAsync();
            _stream.SetThumbnailUrl(_stream.Folder.GetThumbnailUrl(_pathsOptions.BaseUrl));
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
                    if (!await IsChannelLiveAsync())
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
        await _channelRepository.SetLiveAsync(_channel.Id, false);
        var duration = (_stream.FinishedAt - _stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        _logger.LogInformation("Stream finished. Total duration: {Duration}.", duration);
    }

    private async Task FinalizePlaylistAsync()
    {
        _segmentDownloader.CloseSegment();
        await _hlsPlaylist.FinalizeAsync();
    }

    private async Task<bool> IsChannelLiveAsync()
    {
        var metadata = await _twitchClient.GetStreamMetadataAsync(_channel.Name, CancellationToken.None);
        return metadata?.TwitchStreamId == _stream.TwitchStreamId;
    }

    private void SetEndReason(SessionEndReason reason) =>
        Interlocked.CompareExchange(ref _finalizeReason, reason, null);

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync();

        await _hlsPlaylist.DisposeAsync();
        _eventBus.UnSubscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
        _cts.Dispose();
    }
}