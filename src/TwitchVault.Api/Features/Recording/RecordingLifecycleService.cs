using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Recording;

public sealed class RecordingLifecycleService(
    IRecordingOrchestrator orchestrator,
    IDataStore dataStore,
    IStreamService streamService,
    ITwitchGqlClient twitchGqlClient,
    ILogger<RecordingLifecycleService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ResumeInterruptedRecordingsAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await orchestrator.ShutdownAllRecordingsAsync();
    }

    // -----------------------------------------------------------------------
    // Startup: resume recordings interrupted by a previous server shutdown.
    // We query for:
    //   1. Channels still marked IsLive = true in the DB (left by ServerShutdown path).
    //   2. Channels that have streams in Status = Interrupted with no FinishedAt
    //      (left by the StreamError path, which already sets IsLive = false).
    // For each candidate channel, we call the Twitch API to verify it's still
    // live. If it is, we hand it to the orchestrator to resume.
    // -----------------------------------------------------------------------
    private async Task ResumeInterruptedRecordingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Collect channels marked live in the DB.
            var liveChannels = await dataStore.QueryAsync(ctx =>
                ctx.Channels
                    .Monitored()
                    .Live()
                    .ToListAsync(cancellationToken));

            // Collect channels that have interrupted/recording streams but whose
            // IsLive flag was already reset (error-interrupted path).
            var interruptedChannelIds = await dataStore.QueryAsync<TwitchVault.Api.Features.Streams.Stream, List<string>>(
                streams => streams
                    .Where(s => (s.Status == StreamStatus.Interrupted || s.Status == StreamStatus.Recording)
                                && s.FinishedAt == null)
                    .Select(s => s.ChannelId)
                    .Distinct()
                    .ToListAsync(cancellationToken));

            // Merge, avoiding duplicates.
            var liveChannelIds = liveChannels.Select(c => c.Id).ToHashSet();
            var missingChannelIds = interruptedChannelIds.Except(liveChannelIds).ToList();

            List<Channel> extraChannels = [];
            if (missingChannelIds.Count > 0)
            {
                extraChannels = await dataStore.QueryAsync<Channel, List<Channel>>(
                    channels => channels
                        .Monitored()
                        .Where(c => missingChannelIds.Contains(c.Id))
                        .ToListAsync(cancellationToken));
            }

            var candidates = liveChannels.Concat(extraChannels).ToList();

            if (candidates.Count == 0)
            {
                logger.LogDebug("RecordingLifecycleService: no interrupted recording sessions to resume.");
                return;
            }

            logger.LogInformation(
                "RecordingLifecycleService: checking {Count} candidate channel(s) for resume...",
                candidates.Count);

            // Batch-verify liveness against the Twitch API.
            var liveResults = await twitchGqlClient.IsChannelLiveAsync(candidates, cancellationToken);

            var resumeCount = 0;
            foreach (var (channel, isLive) in liveResults)
            {
                if (isLive)
                {
                    try
                    {
                        logger.LogInformation(
                            "Resuming recording for '{Channel}' (was interrupted by previous shutdown).",
                            channel.Name);

                        await orchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
                        resumeCount++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to resume recording for channel '{Channel}'.", channel.Name);
                    }
                }
                else
                {
                    // Channel is offline — reset any stale DB state.
                    await ResetStaleChannelAsync(channel, cancellationToken);
                }
            }

            logger.LogInformation(
                "RecordingLifecycleService: resumed {Resumed}/{Total} recording session(s).",
                resumeCount, candidates.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RecordingLifecycleService: error during recording resume on startup.");
        }
    }

    private async Task ResetStaleChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Channel '{Channel}' is no longer live. Resetting stale recording state.",
                channel.Name);

            await dataStore.ExecuteAsync(() =>
            {
                channel.SetLive(false);
                dataStore.Save(channel);
                return Task.CompletedTask;
            });

            await streamService.ResetStaleStreamsAsync(channel.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reset stale state for channel '{Channel}'.", channel.Name);
        }
    }
}
