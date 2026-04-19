using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public record struct BackgroundRecorder(StreamRecordingSession Session, Task BackgroundTask);

public class StreamController
{
    private readonly ConcurrentDictionary<string, BackgroundRecorder> _activeSessions = new();
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);

    private readonly ChannelRepository channelRepository;
    private readonly StreamRepository streamRepository;
    private readonly SettingsService settingsService;
    private readonly IServiceProvider serviceProvider;
    private readonly IOptions<PathsOptions> pathsOptions;

    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly ILogger<StreamController> logger;

    public StreamController(
        ChannelRepository channelRepository,
        StreamRepository streamRepository,
        SettingsService settingsService,
        IServiceProvider serviceProvider,
        IOptions<PathsOptions> pathsOptions,
        IHostApplicationLifetime applicationLifetime,
        ILogger<StreamController> logger)
    {
        this.channelRepository = channelRepository;
        this.streamRepository = streamRepository;
        this.settingsService = settingsService;
        this.serviceProvider = serviceProvider;
        this.pathsOptions = pathsOptions;
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;

        _ = ResetStaleChannelsAsync();

        applicationLifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                // Wait for all sessions to finish their FinalizeAsync
                var tasks = _activeSessions.Values
                    .Select(r => r.BackgroundTask)
                    .Where(t => t != null)
                    .ToArray();

                if (tasks.Length > 0)
                    Task.WaitAll(tasks);
            }
            catch (AggregateException) { }
        });
    }

    public async Task StartAsync(Channel channel, StreamMetadata metadata)
    {
        await _sessionsLock.WaitAsync();
        try
        {
            await ResetStaleStreamsAsync(channel.ChannelId, metadata.TwitchStreamId);

            if (_activeSessions.Values.Any(recorder => recorder.Session.Channel.ChannelId == channel.ChannelId))
                return;

            var existingStream = (await streamRepository.GetByChannelIdAsync(channel.ChannelId))
                .FirstOrDefault(s => s.TwitchStreamId == metadata.TwitchStreamId);

            if (existingStream is null)
            {
                await StartNewSessionAsync(channel, metadata);
                return;
            }

            if (existingStream.Status is not (StreamStatus.Interrupted or StreamStatus.Recording))
                return;

            await ResumeSessionAsync(existingStream, channel);
            return;
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
            logger.LogWarning("StopRecording: no active session for stream {StreamId}.", streamId);
            return;
        }
        await session.StopAsync();
    }

    public async Task ToggleStreamDeletionAsync(string streamId, bool markForDeletion)
    {
        var session = GetSession(streamId);
        if (session is null)
        {
            logger.LogWarning("MarkForDeletion: no active session for stream {StreamId}.", streamId);
            return;
        }
        await session.ToggleStreamDeletion(markForDeletion);
    }

    public StreamRecordingSession? GetSession(string streamId)
    {
        _activeSessions.TryGetValue(streamId, out var recorder);
        return recorder.Session;
    }

    private async Task StartNewSessionAsync(Channel channel, StreamMetadata metadata)
    {
        var stream = new Models.Stream
        {
            ChannelId = channel.ChannelId,
            TwitchStreamId = metadata.TwitchStreamId,
            Title = metadata.Title,
            GameDisplayName = metadata.GameDisplayName,
            ThumbnailUrl = metadata.PreviewImageUrl,
            FolderPath = BuildFolderPath(channel.Name),
            Status = StreamStatus.Recording
        };

        await streamRepository.AddAsync(stream);
        await channelRepository.SetLiveAsync(channel.ChannelId, true);
        await channelRepository.UpdateLastStreamedAtAsync(channel.ChannelId, stream.StartedAt);

        logger.LogInformation("Starting new session for channel {Name}.", channel.Name);
        StartRecordingSession(stream, channel);
    }

    public async Task ResumeSessionAsync(Models.Stream stream, Channel channel)
    {
        stream.Status = StreamStatus.Recording;
        stream.FinishedAt = null;

        await streamRepository.UpdateAsync(stream);
        await channelRepository.SetLiveAsync(channel.ChannelId, true);

        StartRecordingSession(stream, channel);
    }

    private void StartRecordingSession(Models.Stream stream, Channel channel)
    {
        var session = CreateRecordingSession(stream, channel);

        var recordTask = Task.Run(async () =>
        {
            try
            {
                await session.StartAsync();
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {

                    logger.LogWarning("Recording session for stream {StreamId} was cancelled.", stream.TwitchStreamId);
                    return;
                }

                logger.LogError(ex, "Unhandled error in recording session for stream {StreamId}.", stream.TwitchStreamId);
            }
            finally
            {
                await DisposeSessionAsync(stream.TwitchStreamId, session);
            }
        });

        _activeSessions[stream.TwitchStreamId] = new(session, recordTask);
    }

    private async Task DisposeSessionAsync(string streamId, StreamRecordingSession session)
    {
        await session.DisposeAsync();

        await _sessionsLock.WaitAsync();
        try
        {
            _activeSessions.TryRemove(streamId, out _);
        }
        finally { _sessionsLock.Release(); }
    }

    private StreamRecordingSession CreateRecordingSession(Models.Stream stream, Channel channel)
    {
        var segmentDownloader = serviceProvider.GetRequiredService<SegmentDownloader>();
        var twitchClient = serviceProvider.GetRequiredService<TwitchClient>();
        var sessionLogger = serviceProvider.GetRequiredService<ILogger<StreamRecordingSession>>();

        return new StreamRecordingSession(
            stream,
            channel,
            segmentDownloader,
            streamRepository,
            channelRepository,
            twitchClient,
            settingsService.Settings,
            pathsOptions.Value,
            sessionLogger,
            applicationLifetime.ApplicationStopping);
    }

    private string BuildFolderPath(string channelName)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        return Path.Combine(pathsOptions.Value.Streams, channelName, timestamp);
    }

    private async Task ResetStaleChannelsAsync()
    {
        var channels = await channelRepository.GetAllAsync();
        foreach (var channel in channels.Where(c => c.IsLive))
        {
            await channelRepository.SetLiveAsync(channel.ChannelId, false);
            logger.LogWarning("Reset stale IsLive flag for channel {Name}.", channel.Name);
        }
    }

    private async Task ResetStaleStreamsAsync(int channelId, string currentTwitchStreamId)
    {
        var stale = (await streamRepository.GetByChannelIdAsync(channelId))
            .Where(stream =>
                stream.Status is not StreamStatus.Finished &&
                stream.TwitchStreamId != currentTwitchStreamId);

        foreach (var stream in stale)
        {
            stream.Status = StreamStatus.Finished;
            stream.FinishedAt = DateTime.Now;
            await streamRepository.UpdateAsync(stream);
            logger.LogInformation("Stream {StreamId} for channel {ChannelId} marked as finished — a new broadcast has started.", stream.TwitchStreamId, channelId);
        }
    }
}