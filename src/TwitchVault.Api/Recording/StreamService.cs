using TwitchVault.Api.Common;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Recording;

public class StreamService(
    IStreamRepository streamRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<StreamService> logger)
{
    public async Task DeleteStreamAsync(string twitchStreamId)
    {
        var stream = await streamRepository.GetStreamByIdAsync(twitchStreamId);
        if (stream == null)
            return;

        await IOUtils.DeleteDirectoryWithRetriesAsync(stream.FolderPath);
        await streamRepository.DeleteStreamAsync(twitchStreamId);

        logger.LogInformation("Storage: Removed stream {StreamId}.", twitchStreamId);
    }

    public async Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null)
    {
        var stale = (await streamRepository.GetStreamsByChannelIdAsync(channelId))
            .Where(stream =>
                stream.FinishedAt is null &&
                (currentTwitchStreamId == null || stream.TwitchStreamId != currentTwitchStreamId));

        foreach (var stream in stale)
        {
            stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
            await streamRepository.UpdateAsync(stream);
            logger.LogInformation("Stream {StreamId} marked as finished (stale).", stream.TwitchStreamId);
        }
    }
}