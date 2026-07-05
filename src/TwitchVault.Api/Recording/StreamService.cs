using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IStreamService
{
    Task<Domain.Stream> CreateAsync(Channel channel, StreamMetadata metadata);
    Task DeleteStreamAsync(string twitchStreamId);
    Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null);
}

public class StreamService(
    IStreamRepository streamRepository,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ILogger<StreamService> logger) : IStreamService
{
    public async Task<Domain.Stream> CreateAsync(Channel channel, StreamMetadata metadata)
    {
        var stream = new Domain.Stream
        {
            ChannelId = channel.Id,
            TwitchStreamId = metadata.TwitchStreamId,
            Folder = StreamFolder.Create(pathsOptions.Value.Streams, channel.Name),
            MarkForDeletion = false,
            StartedAt = dateTimeProvider.DateTimeNow
        };

        stream.SetThumbnailUrl(metadata.PreviewImageUrl);
        stream.AddChapter(metadata.Title, metadata.CategoryName, stream.StartedAt);
        await streamRepository.AddAsync(stream);

        return stream;
    }

    public async Task DeleteStreamAsync(string twitchStreamId)
    {
        var stream = await streamRepository.GetStreamByIdAsync(twitchStreamId);
        if (stream == null)
            return;

        await IOUtils.DeleteDirectoryWithRetriesAsync(stream.Folder.RelativePath);
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