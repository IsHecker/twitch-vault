using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IStreamService
{
    Domain.Stream CreateStream(Channel channel, StreamMetadata metadata);
    Task<Result> DeleteStreamAsync(string twitchStreamId);
    Task<Result> DeleteStreamAsync(Domain.Stream stream);
    Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null);
}

public class StreamService(
    IDataStore dataStore,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ILogger<StreamService> logger) : IStreamService
{
    public Domain.Stream CreateStream(Channel channel, StreamMetadata metadata)
    {
        var folder = StreamFolder.Create(pathsOptions.Value.Streams, channel.Name);
        return Domain.Stream.Create(
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
            var stream = await dataStore.QueryAsync<Domain.Stream, Domain.Stream?>(
                streams => streams.GetByIdAsync(twitchStreamId));

            return await DeleteStreamAsync(stream);
        });

    public Task<Result> DeleteStreamAsync(Domain.Stream? stream) =>
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
            var stale = await dataStore.QueryAsync<Domain.Stream, List<Domain.Stream>>(
                streams => streams.StaleActive(channelId, currentTwitchStreamId).ToListAsync());

            foreach (var stream in stale)
            {
                stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
                logger.LogInformation("Stream {StreamId} marked as finished (stale).", stream.Id);
            }
        });

    private Result RequestDeletion(Domain.Stream? stream)
    {
        if (stream == null || stream.IsDeleted)
            return Error.NotFound();

        if (stream.Status == StreamStatus.Recording)
            return Error.Validation("Cannot delete a stream that is still recording or finishing. Stop it first.");

        stream.RequestDeletion();
        dataStore.Save(stream);

        return Result.Success;
    }
}