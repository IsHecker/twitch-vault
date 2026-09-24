using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Recording;

public sealed class RecordingLifecycleService(
    IRecordingOrchestrator orchestrator,
    IDataStore dataStore,
    ITwitchGqlClient twitchGqlClient,
    ILogger<RecordingLifecycleService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ResumeInterruptedRecordingsAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StopAllRecordingsAsync();
    }

    private async Task ResumeInterruptedRecordingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var interruptedStreams = await dataStore.QueryAsync(
                ctx => ctx.Streams
                    .Where(s => s.Status != StreamStatus.Finished && s.ChannelId != null)
                    .Include(s => s.Channel)
                    .ToListAsync(cancellationToken));

            if (interruptedStreams.Count == 0)
                return;

            var channels = interruptedStreams.Select(s => s.Channel).ToList();
            var liveResults = await twitchGqlClient.IsChannelLiveAsync(channels, cancellationToken);

            var resumeCount = 0;
            foreach (var (channel, isLive) in liveResults)
            {
                if (!isLive)
                {
                    await ResetStaleChannelAsync(channel);
                    var stream = interruptedStreams.First(s => s.ChannelId == channel.Id);
                    continue;
                }

                try
                {
                    await orchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
                    resumeCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to resume recording for channel '{Channel}'.", channel.Name);
                }
            }

            logger.LogInformation("Resumed {Resumed}/{Total} recording session(s).",
                resumeCount, interruptedStreams.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during recording resume on startup.");
        }
    }

    private async Task ResetStaleChannelAsync(Channel channel)
    {
        try
        {
            await dataStore.ExecuteAsync(() =>
            {
                channel.SetLive(false);
                dataStore.Save(channel);
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reset stale state for channel '{Channel}'.", channel.Name);
        }
    }
}