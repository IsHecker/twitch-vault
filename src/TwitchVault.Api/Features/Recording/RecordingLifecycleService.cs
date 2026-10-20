using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Recording;

public sealed class RecordingLifecycleService(
    IRecordingOrchestrator orchestrator,
    IDataStore dataStore,
    ITwitchGqlClient twitchGqlClient,
    IStreamFinalizer streamFinalizer,
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
                    var stream = interruptedStreams.First(s => s.ChannelId == channel.Id);
                    await streamFinalizer.FinalizeAsync(
                        channel,
                        stream,
                        stream.SizeBytes,
                        new SessionEndReason.StreamEnded());

                    continue;
                }

                await orchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
                resumeCount++;
            }

            logger.LogInformation("Resumed {Resumed}/{Total} recording session(s).",
                resumeCount, interruptedStreams.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during recording resume on startup.");
        }
    }
}