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
                    .Where(s => s.Status != StreamStatus.Finished && s.Status != StreamStatus.Finalizing && s.ChannelId != null)
                    .Include(s => s.Channel)
                    .ToListAsync(cancellationToken));

            if (interruptedStreams.Count == 0)
                return;

            var distinctChannels = interruptedStreams
                .Select(s => s.Channel)
                .Where(c => c is not null)
                .DistinctBy(c => c.Id)
                .ToList();

            var liveResults = await twitchGqlClient.IsChannelLiveAsync(distinctChannels, cancellationToken);

            var resumeCount = 0;
            foreach (var (channel, isLive) in liveResults)
            {
                var stream = interruptedStreams.First(s => s.ChannelId == channel.Id);

                if (!isLive)
                {
                    await streamFinalizer.FinalizeAsync(
                        channel,
                        stream,
                        stream.SizeBytes,
                        new SessionEndReason.StreamEnded());

                    continue;
                }

                var diskSize = ComputeFolderSizeBytes(stream.Folder.AbsolutePath);
                stream.SetSize(diskSize);

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

    private static long ComputeFolderSizeBytes(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        return new DirectoryInfo(folderPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }
}