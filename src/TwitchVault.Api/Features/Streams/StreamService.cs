using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Streams;

public interface IStreamService
{
    Stream CreateStream(Channel channel, StreamMetadata metadata);
    Task<Result> DeleteStreamAsync(string twitchStreamId);
    Task<Result> DeleteStreamAsync(Stream stream);
    Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null);
}

public class StreamService(
    IDataStore dataStore,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ILogger<StreamService> logger) : IStreamService
{
    public Stream CreateStream(Channel channel, StreamMetadata metadata)
    {
        var folder = StreamFolder.Create(pathsOptions.Value.Streams, channel.Name);
        return Stream.Create(
            metadata.Id,
            channel.Id,
            folder,
            metadata.StartedAt,
            metadata.Title,
            metadata.CategoryId);
    }

    public Task<Result> DeleteStreamAsync(string twitchStreamId) =>
        dataStore.ExecuteAsync(async () =>
        {
            var stream = await dataStore.QueryAsync<Stream, Stream?>(
                streams => streams.GetByIdAsync(twitchStreamId));

            return await DeleteStreamAsync(stream);
        });

    public Task<Result> DeleteStreamAsync(Stream? stream) =>
        dataStore.ExecuteAsync(() =>
        {
            if (stream == null || stream.IsDeleted)
                return Task.FromResult((Result)Error.NotFound());

            if (stream.Status == StreamStatus.Recording)
                return Task.FromResult((Result)Error.Validation("Cannot delete a stream that is still recording or finishing. Stop it first."));

            stream.RequestDeletion();
            dataStore.Save(stream);

            return Task.FromResult(Result.Success);
        });

    public Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null) =>
        dataStore.ExecuteAsync(async () =>
        {
            var stale = await dataStore.QueryAsync<Stream, List<Stream>>(
                streams => streams.StaleActive(channelId, currentTwitchStreamId).ToListAsync());

            foreach (var stream in stale)
            {
                stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
                logger.LogInformation("Stream {StreamId} marked as finished (stale).", stream.Id);
            }
        });
}