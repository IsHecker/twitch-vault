using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Recording;

public class ChannelService(
    AppDbContext db,
    ITwitchGqlClient twitchGqlClient,
    TwitchSubscriptionService twitchSubscription,
    IRecordingOrchestrator recordingOrchestrator,
    IOptions<PathsOptions> pathsOptions) : IChannelService
{
    public async Task<Result<Channel>> AddChannelAsync(
        string channelName,
        int qualityRank,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        var existingChannel = await db.Channels.GetByNameAsync(channelName, cancellationToken);
        if (existingChannel is not null)
            return Error.Conflict($"Channel '{channelName}' is already being monitored.");

        var channelId = await twitchGqlClient.GetChannelIdAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound($"Channel '{channelName}' was not found on Twitch.");

        var channel = Channel.Create(channelId, channelName, qualityRank, isArchived);

        if (isArchived)
            _ = twitchSubscription.AddChannelsAsync([channel], cancellationToken);

        db.Channels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);
        return channel;
    }

    public async Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Result.Failure(Error.NotFound($"Channel '{channelId}' was not found."));

        var streams = await db.Streams.ForChannel(channelId).ToListAsync(cancellationToken);
        if (channel.IsLive)
        {
            var stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
            if (stream is not null)
            {
                await recordingOrchestrator.StopRecordingAsync(stream.Id);
            }
        }

        await db.Channels.Where(c => c.Id == channelId).ExecuteDeleteAsync(cancellationToken);
        await IOUtils.DeleteDirectoryWithRetriesAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
        _ = twitchSubscription.RemoveChannelAsync(channel, cancellationToken);

        return Result.Success;
    }

    public async Task<Result> SetRecordingStatusAsync(string channelId, bool isArchived, CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Result.Failure(Error.NotFound($"Channel '{channelId}' was not found."));

        if (channel.IsArchived == isArchived)
            return Result.Failure(Error.Validation($"Recording status is already set to {isArchived}."));

        channel.SetArchivingStatus(isArchived);
        await db.SaveChangesAsync(cancellationToken);

        if (isArchived)
            _ = twitchSubscription.AddChannelsAsync([channel], cancellationToken);
        else
            _ = twitchSubscription.RemoveChannelAsync(channel, cancellationToken);

        return Result.Success;
    }

    public async Task<Result<Channel>> UpdateChannelQualityAsync(string channelId, int qualityRank, CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, cancellationToken);
        if (channel is null)
            return Result.Failure<Channel>(Error.NotFound($"Channel '{channelId}' was not found."));

        channel.UpdateQualityRank(qualityRank);
        await db.SaveChangesAsync(cancellationToken);
        return channel;
    }
}