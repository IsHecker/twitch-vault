using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IStreamService
{
    Task<Domain.Stream> CreateAsync(Channel channel, StreamMetadata metadata);
    Task<Result> DeleteStreamAsync(string twitchStreamId);
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
            StartedAt = metadata.StartedAt
        };

        stream.AddChapter(metadata.Title, metadata.CategoryId, stream.StartedAt);
        await streamRepository.AddAsync(stream);

        return stream;
    }

    public async Task<Result> DeleteStreamAsync(string twitchStreamId)
    {
        var stream = await streamRepository.GetByIdAsync(twitchStreamId);
        if (stream == null || stream.IsDeleted())
            return Error.NotFound();

        if (stream.Status == StreamStatus.Recording)
            return Error.Validation("Cannot delete a stream that is still recording or finishing. Stop it first.");

        stream.SetStorageOperationStatus(StorageOperationStatus.DeleteRequest);
        await streamRepository.UpdateAsync(stream);
        return Result.Success;
    }

    public async Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null)
    {
        var stale = (await streamRepository.ListByChannelIdAsync(channelId))
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