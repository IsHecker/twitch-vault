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
    IDbContextFactory<AppDbContext> contextFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ILogger<StreamService> logger) : IStreamService
{
    public Domain.Stream CreateStream(Channel channel, StreamMetadata metadata)
    {
        var folder = StreamFolder.Create(pathsOptions.Value.Streams, channel.Name);
        return Domain.Stream.Create(
            metadata.TwitchStreamId,
            channel.Id,
            folder,
            metadata.StartedAt,
            metadata.Title,
            metadata.CategoryId);
    }

    public async Task<Result> DeleteStreamAsync(string twitchStreamId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var stream = await db.Streams.GetByIdAsync(twitchStreamId);
        return await DeleteStreamAsync(db, stream);
    }

    public async Task<Result> DeleteStreamAsync(Domain.Stream? stream)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await DeleteStreamAsync(db, stream);
    }

    public async Task ResetStaleStreamsAsync(string channelId, string? currentTwitchStreamId = null)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var stale = await db.Streams
            .StaleActive(channelId, currentTwitchStreamId)
            .ToListAsync();

        if (stale.Count == 0)
            return;

        foreach (var stream in stale)
        {
            stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
            logger.LogInformation("Stream {StreamId} marked as finished (stale).", stream.Id);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Result> DeleteStreamAsync(AppDbContext db, Domain.Stream? stream)
    {
        if (stream == null || stream.IsDeleted)
            return Error.NotFound();

        if (stream.Status == StreamStatus.Recording)
            return Error.Validation("Cannot delete a stream that is still recording or finishing. Stop it first.");

        db.Attach(stream);
        stream.RequestDeletion();
        await db.SaveChangesAsync();

        return Result.Success;
    }
}