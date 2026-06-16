using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.ChannelMonitor;

[DisallowConcurrentExecution]
public sealed class ChannelMonitorJob(
    StreamController streamController,
    ChannelRepository channelRepository,
    ITwitchGqlClient twitchGqlClient,
    SettingsService settingsService,
    ILogger<ChannelMonitorJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.ChannelMonitor.Enabled)
            return;
        try
        {
            var channels = (await channelRepository.GetAllAsync())
                .Where(channel => !channel.IsLive && channel.ShouldRecord)
                .ToList();

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

        var results = await twitchGqlClient.GetStreamMetadataAsync(channels, cancellationToken);

        foreach (var (channel, metadata) in results)
        {
            if (metadata is null)
                continue;

            try
            {
                await streamController.HandleStreamOnlineAsync(channel.ChannelId, channel.Name, metadata.Value);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start recorder for channel {Name}", channel.Name);
            }
        }
    }
}