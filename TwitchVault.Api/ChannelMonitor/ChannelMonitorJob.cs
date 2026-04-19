using Quartz;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.ChannelMonitor;

[DisallowConcurrentExecution]
public sealed class ChannelMonitorJob(
    StreamController streamController,
    ChannelRepository channelRepository,
    TwitchClient twitchClient,
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
                .Where(channel => !channel.IsLive)
                .ToArray();

            foreach (var channel in channels)
            {
                await PollChannelAsync(channel, context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load channels.");
            return;
        }
    }

    private async Task PollChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.Name))
            return;

        try
        {
            var metadata = await twitchClient.GetStreamMetadataAsync(channel.Name, cancellationToken);
            if (metadata is not null)
            {
                logger.LogInformation("Channel {Name} is live.", channel.Name);
                await streamController.StartAsync(channel, metadata.Value);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to poll channel {Name}.", channel.Name);
        }
    }
}