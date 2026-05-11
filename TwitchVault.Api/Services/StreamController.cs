using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.TwitchEventSub;
using Serilog.Context;
using System.Threading.Tasks;

namespace TwitchVault.Api.Services;

// public record struct BackgroundRecorder(StreamRecordingSession Session, Task BackgroundTask);

// public class StreamController
// {
//     private readonly ConcurrentDictionary<string, BackgroundRecorder> _activeSessions = new();
//     private readonly SemaphoreSlim _sessionsLock = new(1, 1);
//     private readonly ConcurrentDictionary<string, SemaphoreSlim> _channelLocks = new();
//     private readonly TwitchClient twitchClient;
//     private readonly ChannelRepository channelRepository;
//     private readonly StreamRepository streamRepository;
//     private readonly StreamService streamService;
//     private readonly SettingsService settingsService;

//     private readonly IServiceProvider serviceProvider;
//     private readonly IOptions<PathsOptions> pathsOptions;

//     private readonly IHostApplicationLifetime applicationLifetime;
//     private readonly ILogger<StreamController> logger;

//     public StreamController(
//         TwitchClient twitchClient,
//         ChannelRepository channelRepository,
//         StreamRepository streamRepository,
//         StreamService streamService,
//         SettingsService settingsService,
//         EventBus eventBus,
//         IServiceProvider serviceProvider,
//         IOptions<PathsOptions> pathsOptions,
//         IHostApplicationLifetime applicationLifetime,
//         ILogger<StreamController> logger)
//     {
//         this.twitchClient = twitchClient;

//         this.channelRepository = channelRepository;
//         this.streamRepository = streamRepository;
//         this.streamService = streamService;
//         this.settingsService = settingsService;

//         this.serviceProvider = serviceProvider;
//         this.pathsOptions = pathsOptions;
//         this.applicationLifetime = applicationLifetime;
//         this.logger = logger;

//         _ = ResetStaleChannelsAsync();

//         applicationLifetime.ApplicationStopping.Register(() =>
//         {
//             try
//             {
//                 // Wait for all sessions to finish their FinalizeAsync
//                 var tasks = _activeSessions.Values
//                     .Select(r => r.BackgroundTask)
//                     .Where(t => t != null)
//                     .ToArray();

//                 if (tasks.Length > 0)
//                 {
//                     logger.LogInformation("Waiting for {Count} recording sessions to shut down...", tasks.Length);
//                     Task.WaitAll(tasks, timeout: TimeSpan.FromSeconds(10));
//                 }
//             }
//             catch (AggregateException) { }
//         });

//         eventBus.Subscribe<StreamOnlineEvent>(SetupNewStreamAsync);
//         eventBus.Subscribe<ChannelUpdateEvent>(HandleMetadataChangeAsync);
//     }

//     private async Task SetupNewStreamAsync(StreamOnlineEvent e)
//     {
//         logger.LogInformation("Online notification received for {Channel}.", e.ChannelName);

//         var channel = await channelRepository.GetByIdAsync(e.ChannelId);
//         if (channel is null)
//         {
//             logger.LogError("Channel '{Name}' not found in database.", e.ChannelName);
//             return;
//         }

//         try
//         {
//             var metadata = await twitchClient.GetStreamMetadataAsync(channel.Name, applicationLifetime.ApplicationStopping);
//             if (metadata is null)
//                 return;

//             await StartAsync(channel, metadata.Value);
//         }
//         catch (Exception ex)
//         {
//             logger.LogWarning(ex, "Failed to initialize recording for '{Name}'.", channel.Name);
//         }
//     }

//     private async Task HandleMetadataChangeAsync(ChannelUpdateEvent e)
//     {
//         var channelLock = _channelLocks.GetOrAdd(e.ChannelId, _ => new SemaphoreSlim(1, 1));
//         await channelLock.WaitAsync();

//         try
//         {
//             var channel = await channelRepository.GetByIdAsync(e.ChannelId);
//             if (!channel!.IsLive)
//             {
//                 logger.LogError("[{Channel}] Metadata change ignored: Channel is not live or not found.", channel.Name);
//                 return;
//             }

//             var activeStream = (await streamRepository.GetStreamsByChannelIdAsync(e.ChannelId))
//                 .OrderByDescending(s => s.StartedAt)
//                 .First();

//             var session = GetSession(activeStream.TwitchStreamId);
//             if (session is null)
//                 return;

//             var activeSegment = activeStream.StreamSegment;
//             if (activeSegment.Title == e.Title && activeSegment.CategoryName == e.CategoryName)
//             {
//                 logger.LogInformation("[{Channel}] Metadata change ignored: Title and Category are identical.", channel.Name);
//                 return;
//             }

//             logger.LogInformation("[{Channel}] Metadata split triggered: '{OldTitle}' ({OldCategory}) -> '{NewTitle}' ({NewCategory})",
//                 channel.Name, activeSegment.Title, activeSegment.CategoryName, e.Title, e.CategoryName);

//             await session.FinishAsync();

//             var nextSegmentNumber = ++activeStream.TotalSegments;
//             activeStream.StreamSegment = new StreamSegment
//             {
//                 StreamId = activeSegment.StreamId,
//                 Title = e.Title,
//                 CategoryName = e.CategoryName,
//                 Status = StreamStatus.Recording,
//                 SegmentNumber = nextSegmentNumber,
//                 MarkForDeletion = false,
//                 FolderPath = Path.Combine(activeStream.FolderPath, nextSegmentNumber.ToString())
//             };

//             await streamRepository.AddSegmentAsync(activeStream.StreamSegment);
//             await streamRepository.UpdateStreamAsync(activeStream);

//             await StartRecordingSessionAsync(activeStream, channel);
//         }
//         finally
//         {
//             channelLock.Release();
//         }
//     }

//     public async Task StartAsync(Channel channel, StreamMetadata metadata)
//     {
//         await _sessionsLock.WaitAsync();
//         try
//         {
//             await streamService.ResetStaleStreamsAsync(channel.ChannelId, metadata.TwitchStreamId);

//             if (_activeSessions.ContainsKey(metadata.TwitchStreamId))
//                 return;

//             var existingStream = (await streamRepository.GetStreamsByChannelIdAsync(channel.ChannelId))
//                 .FirstOrDefault(s => s.TwitchStreamId == metadata.TwitchStreamId);

//             if (existingStream is null)
//             {
//                 await StartNewStreamSessionAsync(channel, metadata);
//                 return;
//             }

//             if (existingStream.StreamSegment.Status is not (StreamStatus.Interrupted or StreamStatus.Recording))
//                 return;

//             await ResumeSessionAsync(existingStream, channel);
//             return;
//         }
//         finally
//         {
//             _sessionsLock.Release();
//         }
//     }

//     public async Task StopRecordingAsync(string streamId)
//     {
//         var session = GetSession(streamId);
//         if (session is null)
//         {
//             logger.LogWarning("StopRecording: No active session for {StreamId}.", streamId);
//             return;
//         }
//         await session.StopAsync();
//     }

//     public async Task FinishRecordingAsync(string streamId)
//     {
//         var session = GetSession(streamId);
//         if (session is null)
//         {
//             logger.LogWarning("FinishRecording: No active session for {StreamId}.", streamId);
//             return;
//         }
//         await session.FinishAsync();
//     }

//     public async Task ToggleStreamDeletionAsync(string streamId, bool markForDeletion)
//     {
//         var session = GetSession(streamId);
//         if (session is null)
//         {
//             logger.LogWarning("MarkForDeletion: No active session for {StreamId}.", streamId);
//             return;
//         }
//         await session.ToggleStreamDeletion(markForDeletion);
//     }

//     public StreamRecordingSession? GetSession(string streamId)
//     {
//         _activeSessions.TryGetValue(streamId, out var recorder);
//         return recorder.Session;
//     }

//     private async Task StartNewStreamSessionAsync(Channel channel, StreamMetadata metadata)
//     {
//         var rootFolderPath = BuildFolderPath(channel.Name);
//         var streamSegment = new StreamSegment
//         {
//             StreamId = metadata.TwitchStreamId,
//             Title = metadata.Title,
//             CategoryName = metadata.CategoryName,
//             ThumbnailUrl = metadata.PreviewImageUrl,
//             Status = StreamStatus.Recording,
//             SegmentNumber = 1,
//             MarkForDeletion = false,
//             FolderPath = Path.Combine(rootFolderPath, "1")
//         };

//         var stream = new Models.Stream
//         {
//             ChannelId = channel.ChannelId,
//             StreamSegment = streamSegment,
//             TwitchStreamId = metadata.TwitchStreamId,
//             FolderPath = rootFolderPath
//         };

//         await streamRepository.AddSegmentAsync(streamSegment);
//         await streamRepository.AddStreamAsync(stream);

//         await channelRepository.SetLiveAsync(channel.ChannelId, true);
//         await channelRepository.UpdateLastStreamedAtAsync(channel.ChannelId, stream.StartedAt);

//         await StartRecordingSessionAsync(stream, channel);
//     }

//     public async Task ResumeSessionAsync(Models.Stream stream, Channel channel)
//     {
//         stream.StreamSegment.Status = StreamStatus.Recording;
//         stream.FinishedAt = null;

//         await streamRepository.UpdateStreamAsync(stream);
//         await streamRepository.UpdateSegmentAsync(stream.StreamSegment);
//         await channelRepository.SetLiveAsync(channel.ChannelId, true);

//         await StartRecordingSessionAsync(stream, channel);
//     }

//     private async Task StartRecordingSessionAsync(Models.Stream stream, Channel channel)
//     {
//         var session = await CreateRecordingSessionAsync(stream, channel);

//         var recordTask = Task.Run(async () =>
//         {
//             using var _ = LogContext.PushProperty("Channel", channel.Name);
//             using var __ = LogContext.PushProperty("StreamId", stream.TwitchStreamId);

//             try
//             {
//                 await session.StartAsync();
//             }
//             catch (Exception ex)
//             {
//                 if (ex is OperationCanceledException)
//                     return;

//                 logger.LogError(ex, "Unhandled error in recording session.");
//             }
//             finally
//             {
//                 await DisposeSessionAsync(stream.TwitchStreamId, session);
//             }
//         });

//         _activeSessions[stream.TwitchStreamId] = new(session, recordTask);
//     }

//     private async Task DisposeSessionAsync(string streamId, StreamRecordingSession session)
//     {
//         await session.DisposeAsync();

//         await _sessionsLock.WaitAsync();
//         try
//         {
//             _activeSessions.TryRemove(streamId, out _);
//         }
//         finally { _sessionsLock.Release(); }
//     }

//     private async Task<StreamRecordingSession> CreateRecordingSessionAsync(Models.Stream stream, Channel channel)
//     {
//         var segmentDownloader = serviceProvider.GetRequiredService<SegmentDownloader>();
//         var twitchClient = serviceProvider.GetRequiredService<TwitchClient>();
//         var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

//         return await StreamRecordingSession.CreateAsync(
//             stream,
//             stream.StreamSegment,
//             channel,
//             segmentDownloader,
//             streamRepository,
//             streamService,
//             channelRepository,
//             twitchClient,
//             settingsService.Settings,
//             pathsOptions.Value,
//             loggerFactory,
//             applicationLifetime.ApplicationStopping);
//     }

//     private string BuildFolderPath(string channelName)
//     {
//         var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
//         return Path.Combine(pathsOptions.Value.Streams, channelName, timestamp);
//     }

//     private async Task ResetStaleChannelsAsync()
//     {
//         var channels = await channelRepository.GetAllAsync();
//         foreach (var channel in channels.Where(c => c.IsLive))
//         {
//             await channelRepository.SetLiveAsync(channel.ChannelId, false);
//             logger.LogInformation("Corrected stale 'IsLive' state for {Name}.", channel.Name);
//         }
//     }
// }



public sealed record BackgroundRecorder(StreamRecordingSession Session, Task BackgroundTask);

public sealed class StreamController : IAsyncDisposable
{
    private readonly Dictionary<string, BackgroundRecorder> _activeSessions = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _channelLocks = new();
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
        eventBus.Subscribe<ChannelUpdateEvent>(OnMetadataChangedAsync);
    }

    private async Task OnStreamOnlineAsync(StreamOnlineEvent e)
    {
        _logger.LogInformation("Online notification received for {Channel}.", e.ChannelName);

        var channel = await _channelRepository.GetByIdAsync(e.ChannelId);
        if (channel is null)
        {
            _logger.LogError("Channel '{Name}' not found in database.", e.ChannelName);
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
            _logger.LogWarning(ex, "Failed to initialize recording for '{Name}'.", channel.Name);
        }
    }

    private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
    {
        var channelLock = _channelLocks.GetOrAdd(e.ChannelId, _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync(_appLifetime.ApplicationStopping);
        try
        {
            var channel = await _channelRepository.GetByIdAsync(e.ChannelId);
            if (!channel!.IsLive)
            {
                _logger.LogWarning(
                    "[{Channel}] Metadata change ignored: channel is not live. Metadata: '{Title}' ({Category})",
                    channel.Name, e.Title, e.CategoryName);
                return;
            }

            var activeStream = (await _streamRepository.GetStreamsByChannelIdAsync(e.ChannelId))
                .OrderByDescending(s => s.StartedAt)
                .First();

            var activeSegment = activeStream.StreamSegment;
            if (activeSegment.Title == e.Title && activeSegment.CategoryName == e.CategoryName)
            {
                _logger.LogInformation("[{Channel}] Metadata change ignored: title and category unchanged.",
                    channel.Name);
                return;
            }

            var isFinished = await FinishRecordingAsync(activeStream.TwitchStreamId);
            if (!isFinished)
            {
                _logger.LogWarning("[{Channel}] Metadata change ignored: No active session is found for stream {streamId}",
                    channel.Name, activeStream.TwitchStreamId);
                return;
            }

            _logger.LogInformation(
                "[{Channel}] Metadata split: '{OldTitle}' ({OldCat}) → '{NewTitle}' ({NewCat})",
                channel.Name,
                activeSegment.Title, activeSegment.CategoryName,
                e.Title, e.CategoryName);

            var nextSegment = await BuildNextSegmentAsync(activeStream, e.Title, e.CategoryName);
            await _streamRepository.AddSegmentAsync(nextSegment);

            await StartRecordingSessionAsync(activeStream, channel);
        }
        catch (OperationCanceledException) { }
        finally
        {
            channelLock.Release();
        }
    }

    public async Task StartAsync(Channel channel, StreamMetadata metadata)
    {
        await _sessionsLock.WaitAsync();
        try
        {
            await _streamService.ResetStaleStreamsAsync(channel.ChannelId, metadata.TwitchStreamId);

            if (_activeSessions.ContainsKey(metadata.TwitchStreamId))
                return;

            var existing = (await _streamRepository.GetStreamsByChannelIdAsync(channel.ChannelId))
                .FirstOrDefault(s => s.TwitchStreamId == metadata.TwitchStreamId);

            if (existing is null)
            {
                await StartNewStreamAsync(channel, metadata);
                return;
            }

            if (existing.StreamSegment.Status is StreamStatus.Interrupted or StreamStatus.Recording)
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

    public async Task<bool> FinishRecordingAsync(string streamId)
    {
        var session = GetSession(streamId);
        if (session is null)
        {
            _logger.LogWarning("FinishRecording: no active session for stream {StreamId}.", streamId);
            return false;
        }
        await session.FinishAsync();
        return true;
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
        lock (_activeSessions)
            return _activeSessions.TryGetValue(streamId, out var r) ? r.Session : null;
    }

    private async Task StartNewStreamAsync(Channel channel, StreamMetadata metadata)
    {
        var rootFolderPath = BuildStreamFolderPath(channel.Name);

        var segment = new StreamSegment
        {
            StreamId = metadata.TwitchStreamId,
            Title = metadata.Title,
            CategoryName = metadata.CategoryName,
            ThumbnailUrl = metadata.PreviewImageUrl,
            Status = StreamStatus.Recording,
            SegmentNumber = 1,
            MarkForDeletion = false,
            FolderPath = Path.Combine(rootFolderPath, "1"),
        };

        var stream = new Models.Stream
        {
            ChannelId = channel.ChannelId,
            StreamSegment = segment,
            TwitchStreamId = metadata.TwitchStreamId,
            FolderPath = rootFolderPath,
        };

        await _streamRepository.AddSegmentAsync(segment);
        await _streamRepository.AddStreamAsync(stream);
        await _channelRepository.SetLiveAsync(channel.ChannelId, true);
        await _channelRepository.UpdateLastStreamedAtAsync(channel.ChannelId, stream.StartedAt);

        await StartRecordingSessionAsync(stream, channel);
    }

    public async Task ResumeStreamAsync(Models.Stream stream, Channel channel)
    {
        stream.StreamSegment.Status = StreamStatus.Recording;
        stream.FinishedAt = null;

        await _streamRepository.UpdateStreamAsync(stream);
        await _streamRepository.UpdateSegmentAsync(stream.StreamSegment);
        await _channelRepository.SetLiveAsync(channel.ChannelId, true);

        await StartRecordingSessionAsync(stream, channel);
    }

    private async Task<StreamSegment> BuildNextSegmentAsync(Models.Stream stream, string title, string categoryName)
    {
        var nextSegmentNumber = ++stream.TotalSegments;
        stream.StreamSegment = new StreamSegment
        {
            StreamId = stream.TwitchStreamId,
            Title = title,
            CategoryName = categoryName,
            Status = StreamStatus.Recording,
            SegmentNumber = nextSegmentNumber,
            MarkForDeletion = false,
            FolderPath = Path.Combine(stream.FolderPath, nextSegmentNumber.ToString())
        };
        await _streamRepository.UpdateStreamAsync(stream);

        return stream.StreamSegment;
    }

    private async Task StartRecordingSessionAsync(Models.Stream stream, Channel channel)
    {
        var session = await CreateSessionAsync(stream, channel);

        var backgroundTask = Task.Run(async () =>
        {
            using var channelContext = LogContext.PushProperty("Channel", channel.Name);
            using var streamContext = LogContext.PushProperty("StreamId", stream.TwitchStreamId);

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
                _channelLocks.TryRemove(channel.ChannelId, out _);
            }
        });

        _activeSessions[stream.TwitchStreamId] = new(session, backgroundTask);
    }

    private async Task<StreamRecordingSession> CreateSessionAsync(Models.Stream stream, Channel channel)
    {
        var segmentDownloader = _serviceProvider.GetRequiredService<SegmentDownloader>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        return await StreamRecordingSession.CreateAsync(
            stream,
            stream.StreamSegment,
            channel,
            segmentDownloader,
            _streamRepository,
            _streamService,
            _channelRepository,
            _twitchClient,
            _settingsService.Settings,
            _pathsOptions.Value,
            loggerFactory,
            _appLifetime.ApplicationStopping);
    }

    private void OnApplicationStopping()
    {
        Task[] tasks = _activeSessions.Values.Select(r => r.BackgroundTask).ToArray();

        if (tasks.Length == 0)
            return;

        _logger.LogInformation("Waiting for {Count} recording session(s) to shut down...", tasks.Length);

        // Run the async wait on a thread-pool thread so we don't block the
        // stopping thread, which can cause a deadlock when sessions themselves
        // need to complete async I/O during finalization.
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
            _logger.LogInformation("Corrected stale IsLive state for {Name}.", channel.Name);
        }
    }

    private string BuildStreamFolderPath(string channelName)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        return Path.Combine(_pathsOptions.Value.Streams, channelName, timestamp);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        await _sessionsLock.WaitAsync();
        try
        {
            foreach (var recorder in _activeSessions.Values)
                await recorder.Session.DisposeAsync();

            _activeSessions.Clear();
        }
        finally
        {
            _sessionsLock.Release();
        }

        _sessionsLock.Dispose();

        foreach (var sem in _channelLocks.Values)
            sem.Dispose();

        _channelLocks.Clear();
    }
}