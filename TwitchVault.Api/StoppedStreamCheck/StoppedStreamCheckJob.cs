using Quartz;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.StoppedStreamCheck;

[DisallowConcurrentExecution]
public sealed class StoppedStreamCheckJob(
    StreamRepository streamRepository,
    ChannelRepository channelRepository,
    TwitchClient twitchClient,
    SettingsService settingsService,
    ILogger<StoppedStreamCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.StoppedStreamCheck.Enabled)
            return;

        var streams = await streamRepository.GetAllAsync();
        var pausedStreams = streams.Where(s => s.Status == StreamStatus.Stopped);

        foreach (var stream in pausedStreams)
        {
            try
            {
                var metadata = await twitchClient.GetStreamMetadataAsync(
                    (await channelRepository.GetByIdAsync(stream.ChannelId))!.Name,
                    context.CancellationToken);

                if (metadata?.TwitchStreamId == stream.TwitchStreamId)
                    continue;

                // Channel went offline or started a different stream — finalize
                stream.Status = StreamStatus.Finished;
                stream.FinishedAt = DateTime.Now;
                await streamRepository.UpdateAsync(stream);

                logger.LogInformation("Paused stream {StreamId} marked as finished — channel is no longer live.", stream.TwitchStreamId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to check paused stream {StreamId}.", stream.TwitchStreamId);
            }
        }
    }
}