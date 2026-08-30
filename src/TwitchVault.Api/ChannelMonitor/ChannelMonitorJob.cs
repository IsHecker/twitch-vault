using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.ChannelMonitor;

[DisallowConcurrentExecution]
public sealed class ChannelMonitorJob(
    IRecordingOrchestrator recordingOrchestrator,
    IDbContextFactory<AppDbContext> contextFactory,
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
            await using var db = await contextFactory.CreateDbContextAsync(context.CancellationToken);
            var channels = await db.Channels
                .Offline()
                .Monitored()
                .ToListAsync(context.CancellationToken);

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