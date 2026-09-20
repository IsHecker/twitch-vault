using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Channels.Jobs;

[DisallowConcurrentExecution]
public sealed class ChannelMonitorJob(
    IRecordingOrchestrator recordingOrchestrator,
    IDataStore dataStore,
    ITwitchGqlClient twitchGqlClient,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    ILogger<ChannelMonitorJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.ChannelMonitor).Enabled)
            return;

        try
        {
            var channels = await dataStore.QueryAsync(db =>
                db.Channels.Offline()
                    .Monitored()
                    .ToListAsync(context.CancellationToken));

            await PollChannelsAsync(channels, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to poll channels.");
            return;
        }
    }

    private async Task PollChannelsAsync(List<Channel> channels, CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
            return;

        var results = await twitchGqlClient.IsChannelLiveAsync(channels, cancellationToken);

        foreach (var (channel, isLive) in results)
        {
            if (!isLive)
                continue;

            try
            {
                logger.LogInformation("Monitor detected channel {Channel} is live", channel.Name);
                await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start recorder for channel {Name}", channel.Name);
            }
        }
    }
}