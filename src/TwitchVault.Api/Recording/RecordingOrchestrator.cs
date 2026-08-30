using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Twitch;
using Serilog.Context;

namespace TwitchVault.Api.Recording;

public sealed class RecordingOrchestrator(
    IStreamRecorderRegistry streamRecorderRegistry,
    IStreamRecorderFactory streamRecorderFactory,
    IDbContextFactory<AppDbContext> contextFactory,
    IStreamService streamService,
    ITwitchGqlClient twitchGqlClient,
    ILogger<RecordingOrchestrator> logger,
    IHostApplicationLifetime appLifetime) : IRecordingOrchestrator
{
    private static readonly SemaphoreSlim _sessionsLock = new(1, 1);

    public async Task TryStartRecordingAsync(string channelId, string channelName)
    {
        if (!streamRecorderRegistry.TryRegister(channelId))
        {
            logger.LogDebug("{Channel} live stream is already being recorded.", channelName);
            return;
        }

        try
        {
            logger.LogInformation("{Channel} went live.", channelName);

            await using var db = await contextFactory.CreateDbContextAsync();
            var channel = await db.Channels.GetByIdAsync(channelId);
            if (channel is null)
            {
                logger.LogWarning("Channel {Channel} not found in database.", channelName);
                streamRecorderRegistry.Remove(channelId);
                return;
            }

            var metadata = await twitchGqlClient.GetStreamMetadataAsync(channelName, default);
            if (!metadata.HasValue)
            {
                logger.LogWarning("Failed to fetch stream metadata for {Channel}.", channelName);
                streamRecorderRegistry.Remove(channelId);
                return;
            }

            await RecordStreamAsync(db, channel, metadata.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            streamRecorderRegistry.Remove(channelId);
            logger.LogError(ex, "Failed to initialize recording for {Channel}.", channelName);
        }
    }

    private async Task RecordStreamAsync(AppDbContext db, Channel channel, StreamMetadata metadata)
    {
        await _sessionsLock.WaitAsync();
        try
        {
            channel.SetLive(true);

            var existing = await db.Streams.GetByIdAsync(metadata.TwitchStreamId);
            var stream = existing switch
            {
                null => await CreateStreamAsync(db, channel, metadata),
                { Status: StreamStatus.Interrupted } => MarkAsResuming(existing),
                _ => null
            };

            if (stream is null)
                return;

            await db.SaveChangesAsync();
            await LaunchRecordingSessionAsync(stream, channel);
        }
        finally
        {
            _sessionsLock.Release();
        }
    }

    public async Task StopRecordingAsync(string channelId)
    {
        if (!streamRecorderRegistry.TryGet(channelId, out var recorder))
        {
            logger.LogWarning("StopRecording: no active session for stream {StreamId}.", channelId);
            return;
        }

        await recorder.StopAsync();
    }

    public async Task<IReadOnlyList<string>> FinishAllRecordingsAsync()
    {
        var activeChannelIds = streamRecorderRegistry.GetActiveChannelIds();
        if (activeChannelIds.Count == 0)
            return [];

        logger.LogInformation("Stopping and finalizing all {Count} active recording session(s)...", activeChannelIds.Count);

        var finishedChannels = new List<string>();
        foreach (var channelId in activeChannelIds)
        {
            if (streamRecorderRegistry.TryGet(channelId, out var recorder))
            {
                await recorder.FinishAsync();
                finishedChannels.Add(channelId);
            }
        }

        var backgroundTasks = streamRecorderRegistry.GetAllBackgroundTasks();
        if (backgroundTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(backgroundTasks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Timeout or error waiting for recording sessions to finish.");
            }
        }

        return finishedChannels;
    }

    private async Task<Domain.Stream> CreateStreamAsync(AppDbContext db, Channel channel, StreamMetadata metadata)
    {
        var stream = streamService.CreateStream(channel, metadata);
        await db.Streams.AddAsync(stream);
        channel.UpdateLastStreamedAt(stream.StartedAt);
        return stream;
    }

    private static Domain.Stream MarkAsResuming(Domain.Stream stream)
    {
        stream.MarkAsRecording();
        return stream;
    }

    private async Task LaunchRecordingSessionAsync(Domain.Stream stream, Channel channel)
    {
        var session = await streamRecorderFactory.CreateAsync(stream, channel, appLifetime.ApplicationStopping);

        var backgroundTask = Task.Run(async () =>
        {
            using var channelContext = LogContext.PushProperty("Channel", channel.Name);
            using var streamContext = LogContext.PushProperty("StreamId", stream.Id);
            using var titleContext = LogContext.PushProperty("Title", stream.CurrentChapter.Title);

            try
            {
                await session.StartAsync(stream, channel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in recording session.");
            }
            finally
            {
                streamRecorderRegistry.Remove(channel.Id);
            }
        });

        streamRecorderRegistry.Register(channel.Id, session, backgroundTask);
    }
}


// public sealed class RecordingOrchestrator(
//     IStreamRecorderRegistry streamRecorderRegistry,
//     IStreamRecorderFactory streamRecorderFactory,
//     IDbContextFactory<AppDbContext> contextFactory,
//     IStreamService streamService,
//     ITwitchGqlClient twitchGqlClient,
//     ILogger<RecordingOrchestrator> logger,
//     IHostApplicationLifetime appLifetime) : IRecordingOrchestrator
// {
//     private static readonly SemaphoreSlim _sessionsLock = new(1, 1);

//     private AppDbContext _dbContext = null!;

//     public async Task TryStartRecordingAsync(string channelId, string channelName)
//     {
//         if (!streamRecorderRegistry.TryRegister(channelId))
//         {
//             logger.LogDebug("{Channel} live stream is already being recorded.", channelName);
//             return;
//         }

//         try
//         {
//             logger.LogInformation("{Channel} went live.", channelName);

//             using var db = await contextFactory.CreateDbContextAsync();
//             _dbContext = db;

//             var channel = await _dbContext.Channels.GetByIdAsync(channelId);
//             if (channel is null)
//             {
//                 logger.LogWarning("Channel {Channel} not found in database.", channelName);
//                 streamRecorderRegistry.Remove(channelId);
//                 return;
//             }

//             var metadata = await twitchGqlClient.GetStreamMetadataAsync(channelName, default);
//             if (!metadata.HasValue)
//             {
//                 logger.LogWarning("Failed to fetch stream metadata for {Channel}.", channelName);
//                 streamRecorderRegistry.Remove(channelId);
//                 return;
//             }

//             await RecordStreamAsync(channel, metadata.Value);
//         }
//         catch (Exception ex) when (ex is not OperationCanceledException)
//         {
//             streamRecorderRegistry.Remove(channelId);
//             logger.LogError(ex, "Failed to initialize recording for {Channel}.", channelName);
//         }
//     }

//     private async Task RecordStreamAsync(Channel channel, StreamMetadata metadata)
//     {
//         await _sessionsLock.WaitAsync();
//         try
//         {
//             channel.SetLive(true);

//             var existing = await _dbContext.Streams.GetByIdAsync(metadata.TwitchStreamId);
//             var stream = existing switch
//             {
//                 null => await CreateStreamAsync(channel, metadata),
//                 { Status: StreamStatus.Interrupted } => MarkAsResuming(existing),
//                 _ => null
//             };

//             if (stream is null)
//                 return;

//             await _dbContext.SaveChangesAsync();
//             await LaunchRecordingSessionAsync(stream, channel);
//         }
//         finally
//         {
//             _sessionsLock.Release();
//         }
//     }

//     public async Task StopRecordingAsync(string channelId)
//     {
//         if (!streamRecorderRegistry.TryGet(channelId, out var recorder))
//         {
//             logger.LogWarning("StopRecording: no active session for stream {StreamId}.", channelId);
//             return;
//         }

//         await recorder.StopAsync();
//     }

//     public async Task<IReadOnlyList<string>> FinishAllRecordingsAsync()
//     {
//         var activeChannelIds = streamRecorderRegistry.GetActiveChannelIds();
//         if (activeChannelIds.Count == 0)
//             return [];

//         logger.LogInformation("Stopping and finalizing all {Count} active recording session(s)...", activeChannelIds.Count);

//         var finishedChannels = new List<string>();
//         foreach (var channelId in activeChannelIds)
//         {
//             if (streamRecorderRegistry.TryGet(channelId, out var recorder))
//             {
//                 await recorder.FinishAsync();
//                 finishedChannels.Add(channelId);
//             }
//         }

//         var backgroundTasks = streamRecorderRegistry.GetAllBackgroundTasks();
//         if (backgroundTasks.Length > 0)
//         {
//             try
//             {
//                 await Task.WhenAll(backgroundTasks);
//             }
//             catch (Exception ex) when (ex is not OperationCanceledException)
//             {
//                 logger.LogWarning(ex, "Timeout or error waiting for recording sessions to finish.");
//             }
//         }

//         return finishedChannels;
//     }

//     private async Task<Domain.Stream> CreateStreamAsync(Channel channel, StreamMetadata metadata)
//     {
//         var stream = await streamService.CreateStreamAsync(channel, metadata);
//         channel.UpdateLastStreamedAt(stream.StartedAt);
//         return stream;
//     }
//     private static Domain.Stream MarkAsResuming(Domain.Stream stream)
//     {
//         stream.MarkAsRecording();
//         return stream;
//     }

//     private async Task LaunchRecordingSessionAsync(Domain.Stream stream, Channel channel)
//     {
//         var session = await streamRecorderFactory.CreateAsync(stream, channel, appLifetime.ApplicationStopping);

//         var backgroundTask = Task.Run(async () =>
//         {
//             using var channelContext = LogContext.PushProperty("Channel", channel.Name);
//             using var streamContext = LogContext.PushProperty("StreamId", stream.Id);
//             using var titleContext = LogContext.PushProperty("Title", stream.CurrentChapter.Title);

//             try
//             {
//                 await session.StartAsync(stream, channel);
//             }
//             catch (Exception ex) when (ex is not OperationCanceledException)
//             {
//                 logger.LogError(ex, "Unhandled error in recording session.");
//             }
//             finally
//             {
//                 streamRecorderRegistry.Remove(channel.Id);
//             }
//         });

//         streamRecorderRegistry.Register(channel.Id, session, backgroundTask);
//     }
// }