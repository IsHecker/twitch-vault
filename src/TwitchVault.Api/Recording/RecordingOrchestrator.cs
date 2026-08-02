using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;
using Serilog.Context;

namespace TwitchVault.Api.Recording;

public sealed class RecordingOrchestrator
{
    private readonly IStreamRecorderRegistry _streamRecorderRegistry;
    private readonly IStreamRecorderFactory _streamRecorderFactory;
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);
    private readonly IChannelRepository _channelRepository;
    private readonly IStreamRepository _streamRepository;
    private readonly IStreamService _streamService;
    private readonly ITwitchGqlClient _twitchGqlClient;
    private readonly ILogger<RecordingOrchestrator> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    public RecordingOrchestrator(
        IStreamRecorderRegistry streamRecorderRegistry,
        IStreamRecorderFactory streamRecorderFactory,
        IChannelRepository channelRepository,
        IStreamRepository streamRepository,
        IStreamService streamService,
        ITwitchGqlClient twitchGqlClient,
        ILogger<RecordingOrchestrator> logger,
        IHostApplicationLifetime appLifetime)
    {
        _streamRecorderRegistry = streamRecorderRegistry;
        _streamRecorderFactory = streamRecorderFactory;
        _channelRepository = channelRepository;
        _streamRepository = streamRepository;
        _streamService = streamService;
        _twitchGqlClient = twitchGqlClient;
        _appLifetime = appLifetime;
        _logger = logger;

        _ = ResetStaleChannelsAsync();
        appLifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    public async Task HandleStreamOnlineAsync(string channelId, string channelName)
    {
        if (!_streamRecorderRegistry.TryRegister(channelId))
        {
            _logger.LogDebug("{Channel} live stream is already being recorded.", channelName);
            return;
        }

        try
        {
            _logger.LogInformation("{Channel} went live.", channelName);
            var channel = await _channelRepository.GetByIdAsync(channelId);
            if (channel is null)
            {
                _logger.LogWarning("Channel {Channel} not found in database.", channelName);
                _streamRecorderRegistry.Remove(channelId);
                return;
            }

            var metadata = await _twitchGqlClient.GetStreamMetadataAsync(channelName, default);
            if (!metadata.HasValue)
            {
                _logger.LogWarning("Failed to fetch stream metadata for {Channel}.", channelName);
                _streamRecorderRegistry.Remove(channelId);
                return;
            }

            await StartAsync(channel, metadata.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _streamRecorderRegistry.Remove(channelId);
            _logger.LogError(ex, "Failed to initialize recording for {Channel}.", channelName);
        }
    }

    public async Task StartAsync(Channel channel, StreamMetadata metadata)
    {
        await _sessionsLock.WaitAsync();
        try
        {
            var existing = (await _streamRepository.ListByChannelIdAsync(channel.Id))
                .FirstOrDefault(s => s.TwitchStreamId == metadata.TwitchStreamId);

            await _channelRepository.SetLiveAsync(channel.Id, true);

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

    public async Task StopRecordingAsync(string channelId)
    {
        if (!_streamRecorderRegistry.TryGet(channelId, out var recorder))
        {
            _logger.LogWarning("StopRecording: no active session for stream {StreamId}.", channelId);
            return;
        }

        await recorder.StopAsync();
    }

    public async Task ToggleStreamDeletionAsync(string channelId, bool markForDeletion)
    {
        if (!_streamRecorderRegistry.TryGet(channelId, out var recorder))
        {
            _logger.LogWarning("ToggleDeletion: no active session for stream {StreamId}.", channelId);
            return;
        }

        await recorder.ToggleStreamDeletionAsync(markForDeletion);
    }

    public async Task ResumeStreamAsync(Domain.Stream stream, Channel channel)
    {
        stream.MarkAsRecording();
        await StartRecordingAsync(stream, channel);
    }

    private async Task StartNewStreamAsync(Channel channel, StreamMetadata metadata)
    {
        var stream = await _streamService.CreateAsync(channel, metadata);
        await _channelRepository.UpdateLastStreamedAtAsync(channel.Id, stream.StartedAt);
        await StartRecordingAsync(stream, channel);
    }

    private async Task StartRecordingAsync(Domain.Stream stream, Channel channel)
    {
        await _streamRepository.UpdateAsync(stream);

        var session = await _streamRecorderFactory.CreateAsync(stream, channel, _appLifetime.ApplicationStopping);
        var backgroundTask = Task.Run(async () =>
        {
            using var channelContext = LogContext.PushProperty("Channel", channel.Name);
            using var streamContext = LogContext.PushProperty("StreamId", stream.TwitchStreamId);
            using var titleContext = LogContext.PushProperty("Title", stream.CurrentChapter.Title);

            try
            {
                await session.StartAsync(stream, channel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in recording session.");
            }
            finally
            {
                _streamRecorderRegistry.Remove(channel.Id);
            }
        });

        _streamRecorderRegistry.Register(channel.Id, session, backgroundTask);
    }

    private void OnApplicationStopping()
    {
        Task[] tasks = _streamRecorderRegistry.GetAllBackgroundTasks();
        if (tasks.Length == 0)
            return;

        _logger.LogDebug("Waiting for {Count} recording session(s) to shut down...", tasks.Length);

        Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session shutdown completed via cancellation (expected).");
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
            await _channelRepository.SetLiveAsync(channel.Id, false);
        }
    }
}