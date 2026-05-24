using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.TwitchEventSub;
using Serilog.Context;

namespace TwitchVault.Api.Services;

public sealed record BackgroundRecorder(StreamRecordingSession Session, Task BackgroundTask);

public sealed class StreamController
{
    private readonly Dictionary<string, BackgroundRecorder> _activeRecorders = [];
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    private readonly TwitchClient _twitchClient;
    private readonly ChannelRepository _channelRepository;
    private readonly StreamRepository _streamRepository;
    private readonly StreamService _streamService;
    private readonly SettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<PathsOptions> _pathsOptions;
    private readonly ILogger<StreamController> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    private bool _isDisposed;

    public StreamController(
        TwitchClient twitchClient,
        ChannelRepository channelRepository,
        StreamRepository streamRepository,
        StreamService streamService,
        SettingsService settingsService,
        EventBus eventBus,
        IServiceProvider serviceProvider,
        IOptions<PathsOptions> pathsOptions,
        ILogger<StreamController> logger,
        IHostApplicationLifetime appLifetime)
    {
        _twitchClient = twitchClient;
        _channelRepository = channelRepository;
        _streamRepository = streamRepository;
        _streamService = streamService;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;
        _pathsOptions = pathsOptions;
        _appLifetime = appLifetime;
        _logger = logger;

        _ = ResetStaleChannelsAsync();
        appLifetime.ApplicationStopping.Register(OnApplicationStopping);

        eventBus.Subscribe<StreamOnlineEvent>(OnStreamOnlineAsync);
    }

    private async Task OnStreamOnlineAsync(StreamOnlineEvent e)
    {
        _logger.LogInformation("{Channel} went live", e.ChannelName);

        var channel = await _channelRepository.GetByIdAsync(e.ChannelId);
        if (channel is null)
        {
            _logger.LogWarning("Channel '{Channel}' not found in database.", e.ChannelName);
            return;
        }

        try
        {
            var metadata = await _twitchClient.GetStreamMetadataAsync(
                channel.Name, _appLifetime.ApplicationStopping);

            if (metadata is null)
                return;

            await StartAsync(channel, metadata.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to initialize recording for '{Channel}'.", channel.Name);
        }
    }

    public async Task StartAsync(Channel channel, StreamMetadata metadata)
    {
        await _sessionsLock.WaitAsync();
        try
        {
            await _streamService.ResetStaleStreamsAsync(channel.ChannelId, metadata.TwitchStreamId);

            if (_activeRecorders.ContainsKey(metadata.TwitchStreamId))
                return;

            var existing = (await _streamRepository.GetStreamsByChannelIdAsync(channel.ChannelId))
                .FirstOrDefault(s => s.TwitchStreamId == metadata.TwitchStreamId);

            if (existing is null)
            {
                await StartNewStreamAsync(channel, metadata);
                return;
            }

            if (existing.Status is StreamStatus.Interrupted or StreamStatus.Recording)
                await ResumeStreamAsync(existing, channel);
        }
        finally
        {
            _sessionsLock.Release();
        }
    }

    public async Task StopRecordingAsync(string streamId)
    {
        var session = GetSession(streamId);
        if (session is null)
        {
            _logger.LogWarning("StopRecording: no active session for stream {StreamId}.", streamId);
            return;
        }
        await session.StopAsync();
    }

    public async Task ToggleStreamDeletionAsync(string streamId, bool markForDeletion)
    {
        var session = GetSession(streamId);
        if (session is null)
        {
            _logger.LogWarning("ToggleDeletion: no active session for stream {StreamId}.", streamId);
            return;
        }
        await session.ToggleStreamDeletion(markForDeletion);
    }

    public StreamRecordingSession? GetSession(string streamId)
    {
        return _activeRecorders.GetValueOrDefault(streamId)?.Session;
    }

    private async Task StartNewStreamAsync(Channel channel, StreamMetadata metadata)
    {
        var rootFolderPath = BuildStreamFolderPath(channel.Name);

        var stream = new Models.Stream
        {
            ChannelId = channel.ChannelId,
            TwitchStreamId = metadata.TwitchStreamId,
            FolderPath = rootFolderPath,
            ThumbnailUrl = metadata.PreviewImageUrl,
            Status = StreamStatus.Recording,
            MarkForDeletion = false,
            Chapters = []
        };

        stream.AddChapter(metadata.Title, metadata.CategoryName);

        await _streamRepository.AddStreamAsync(stream);
        await _channelRepository.SetLiveAsync(channel.ChannelId, true);
        await _channelRepository.UpdateLastStreamedAtAsync(channel.ChannelId, stream.StartedAt);

        await StartRecordingSessionAsync(stream, channel);
    }

    public async Task ResumeStreamAsync(Models.Stream stream, Channel channel)
    {
        stream.FinishedAt = null;

        await _streamRepository.UpdateAsync(stream);
        await _channelRepository.SetLiveAsync(channel.ChannelId, true);

        await StartRecordingSessionAsync(stream, channel);
    }

    private async Task StartRecordingSessionAsync(Models.Stream stream, Channel channel)
    {
        var session = await CreateSessionAsync(stream, channel);

        var backgroundTask = Task.Run(async () =>
        {
            using var channelContext = LogContext.PushProperty("Channel", channel.Name);
            using var streamContext = LogContext.PushProperty("StreamId", stream.TwitchStreamId);
            using var titleContext = LogContext.PushProperty("Title", stream.CurrentChapter.Title);

            try
            {
                await session.StartAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in recording session.");
            }
            finally
            {
                _activeRecorders.Remove(stream.TwitchStreamId);
            }
        });

        _activeRecorders[stream.TwitchStreamId] = new(session, backgroundTask);
    }

    private async Task<StreamRecordingSession> CreateSessionAsync(Models.Stream stream, Channel channel)
    {
        var segmentDownloader = _serviceProvider.GetRequiredService<SegmentDownloader>();
        var eventBus = _serviceProvider.GetRequiredService<EventBus>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        return await StreamRecordingSession.CreateAsync(
            stream,
            channel,
            segmentDownloader,
            _streamRepository,
            _streamService,
            _channelRepository,
            _twitchClient,
            _settingsService.Settings,
            _pathsOptions.Value,
            eventBus,
            loggerFactory,
            _appLifetime.ApplicationStopping);
    }

    private void OnApplicationStopping()
    {
        Task[] tasks = _activeRecorders.Values.Select(r => r.BackgroundTask).ToArray();

        if (tasks.Length == 0)
            return;

        _logger.LogDebug("Waiting for {Count} recording session(s) to shut down...", tasks.Length);

        Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out waiting for recording sessions to finish.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session shutdown.");
            }
        }).GetAwaiter().GetResult();
    }

    private async Task ResetStaleChannelsAsync()
    {
        var channels = await _channelRepository.GetAllAsync();
        foreach (var channel in channels.Where(c => c.IsLive))
        {
            await _channelRepository.SetLiveAsync(channel.ChannelId, false);
        }
    }

    private string BuildStreamFolderPath(string channelName)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        return Path.Combine(_pathsOptions.Value.Streams, channelName, timestamp);
    }
}