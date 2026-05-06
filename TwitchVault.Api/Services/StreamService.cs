using TwitchVault.Api.Common;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Services;

public class StreamService(
    StreamRepository streamRepository,
    ILogger<StreamService> logger)
{
    public async Task DeleteStreamAsync(string twitchStreamId)
    {
        var stream = await streamRepository.GetStreamByIdAsync(twitchStreamId);
        if (stream == null) return;

        await IOUtils.DeleteDirectoryWithRetriesAsync(stream.FolderPath);
        await streamRepository.DeleteStreamAsync(twitchStreamId);
        logger.LogInformation("Storage: Removed stream {StreamId}.", twitchStreamId);
    }

    public async Task DeleteSegmentAsync(string twitchStreamId, string segmentId)
    {
        var stream = await streamRepository.GetStreamByIdAsync(twitchStreamId);
        if (stream == null)
            return;

        var segments = await streamRepository.GetSegmentsByStreamIdAsync(twitchStreamId);
        var segment = segments.FirstOrDefault(s => s.Id == segmentId);
        if (segment == null)
            return;

        await IOUtils.DeleteDirectoryWithRetriesAsync(segment.FolderPath);
        await streamRepository.DeleteSegmentAsync(segment);

        if (--stream.TotalSegments == 0)
        {
            await streamRepository.DeleteStreamAsync(twitchStreamId);
            return;
        }

        if (stream.StreamSegment.Id == segmentId)
        {
            var remainingSegments = await streamRepository.GetSegmentsByStreamIdAsync(twitchStreamId);
            stream.StreamSegment = remainingSegments.OrderBy(s => s.SegmentNumber).First();
            await streamRepository.UpdateStreamAsync(stream);
        }

        logger.LogInformation("Storage: Removed segment {SegmentId} from '{title}'.", segmentId, segment.Title);
    }

    public async Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null)
    {
        var stale = (await streamRepository.GetStreamsByChannelIdAsync(channelId))
            .Where(stream =>
                stream.FinishedAt is null &&
                (currentTwitchStreamId == null || stream.TwitchStreamId != currentTwitchStreamId));

        foreach (var stream in stale)
        {
            var segments = await streamRepository.GetSegmentsByStreamIdAsync(stream.TwitchStreamId);
            var lastSegment = segments.LastOrDefault();
            lastSegment?.MarkAsFinished();
            stream.FinishedAt = DateTime.Now;

            await streamRepository.UpdateStreamAsync(stream);
            if (lastSegment != null)
                await streamRepository.UpdateSegmentAsync(lastSegment);

            logger.LogInformation("Stream {StreamId} marked as finished (stale).", stream.TwitchStreamId);
        }
    }
}
