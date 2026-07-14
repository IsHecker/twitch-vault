using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorder : IAsyncDisposable
{
    Task StartAsync(Domain.Stream stream, Channel channel);
    Task StopAsync();
    Task ToggleStreamDeletionAsync(bool markForDeletion);
}

public sealed class StreamRecorder : IStreamRecorder
{
    private Domain.Stream _stream = null!;
    private Channel _channel = null!;
    private readonly IThumbnailManager _thumbnailManager;
    private readonly IManifestPoller _manifestPoller;
    private readonly ISegmentDownloader _segmentDownloader;
    private readonly IHlsPlaylist _hlsPlaylist;
    private readonly IStreamRepository _streamRepository;
    private readonly IStreamFinalizer _finalizer;
    private readonly AppSettings _settings;
    private readonly EventBus _eventBus;
    private readonly ILogger<StreamRecorder> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IDateTimeProvider _dateTimeProvider;
    private SessionEndReason? _finalizeReason;

    public StreamRecorder(
        IThumbnailManager thumbnailManager,
        IManifestPoller manifestPoller,
        ISegmentDownloader segmentDownloader,
        IHlsPlaylist hlsPlaylist,
        IStreamRepository streamRepository,
        IStreamFinalizer finalizer,
        SettingsService settingsService,
        EventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        ILogger<StreamRecorder> logger,
        CancellationToken parentCancellationToken)
    {
        _segmentDownloader = segmentDownloader;
        _manifestPoller = manifestPoller;
        _thumbnailManager = thumbnailManager;
        _streamRepository = streamRepository;
        _finalizer = finalizer;
        _settings = settingsService.Settings;
        _hlsPlaylist = hlsPlaylist;
        _eventBus = eventBus;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);

        eventBus.Subscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
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
            SetEndReason(new SessionEndReason.StreamError(ex));
            _logger.LogError(ex, "Unhandled error in recording session.");
        }
        finally
        {
            await _finalizer.FinalizeAsync(
                _stream,
                _channel,
                _segmentDownloader,
                _finalizeReason ?? new SessionEndReason.StreamEnded());

            await DisposeAsync();
        }
    }

    public async Task StopAsync()
    {
        SetEndReason(new SessionEndReason.StreamStopped());
        await _cts.CancelAsync();
    }

    public async Task ToggleStreamDeletionAsync(bool markForDeletion)
    {
        _stream.MarkForDeletion = markForDeletion;
        await _streamRepository.UpdateAsync(_stream);
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

    private async Task RecordStreamAsync(CancellationToken cancellationToken)
    {
        int consecutiveNetworkErrors = 0;
        int emptyPollsRemaining = _settings.Vault.MaxConsecutiveEmptyPolls;

        while (!cancellationToken.IsCancellationRequested && emptyPollsRemaining > 0)
        {
            try
            {
                await _thumbnailManager.TryCaptureSnapshotAsync(_stream);

                var (manifest, hasQualityChanged) = await _manifestPoller
                    .GetNextManifestAsync(_channel.Name, cancellationToken);

                if (string.IsNullOrWhiteSpace(manifest))
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
                    SetEndReason(new SessionEndReason.StreamEnded());
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