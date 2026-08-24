using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
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
        var folder = StreamFolder.Create(pathsOptions.Value.Streams, channel.Name);
        var stream = Domain.Stream.Create(
            metadata.TwitchStreamId,
            channel.Id,
            folder,
            metadata.StartedAt,
            metadata.Title,
            metadata.CategoryId);

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

        stream.RequestDeletion();
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