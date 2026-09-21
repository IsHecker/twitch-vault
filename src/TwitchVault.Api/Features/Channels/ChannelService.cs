using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Extensions;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Channels;

public sealed class ChannelService(
    AppDbContext db,
    ITwitchGqlClient twitchGqlClient,
    ITwitchSubscriptionService twitchSubscription,
    IRecordingOrchestrator recordingOrchestrator,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ICurrentUser currentUser,
    IOptionsMonitor<VaultOptions> vaultOptions) : IChannelService
{
    public async Task<Result<Channel>> AddChannelAsync(
        string channelName,
        int? qualityRank,
        bool? isArchived,
        CancellationToken cancellationToken = default)
    {
        if (!TwitchLogin.TryNormalize(channelName, out var login))
            return Error.Validation("Invalid Twitch channel name.");

        var options = vaultOptions.CurrentValue;

        var effectiveQualityRank = currentUser.IsAdmin && qualityRank.HasValue
            ? qualityRank.Value
            : options.DefaultQualityRank;

        var qualityRankResult = ValidateQualityRank(effectiveQualityRank);
        if (qualityRankResult.IsFailure)
            return qualityRankResult.Error;

        var channelId = await twitchGqlClient.GetChannelIdAsync(login, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound("Channel doesn't exist on Twitch.");

        var isBanned = await db.BannedChannels.AnyAsync(b => b.Id == channelId, cancellationToken);
        if (isBanned && !currentUser.IsAdmin)
            return Error.Forbidden("This channel has been banned by an administrator.");

        var alreadyMonitoring = await db.Subscriptions
            .AnyAsync(uc => uc.UserId == currentUser.Id && uc.ChannelId == channelId, cancellationToken);

        if (alreadyMonitoring)
            return Error.Conflict("You are already subscribed to this channel.");

        if (!currentUser.IsAdmin)
        {
            var currentSubscriptions = await db.Subscriptions
                .CountAsync(uc => uc.UserId == currentUser.Id, cancellationToken);

            if (currentSubscriptions >= options.MaxSubscriptionsPerUser)
                return Error.Validation(
                    $"Subscription limit reached. You cannot subscribe to more than {options.MaxSubscriptionsPerUser} channels.");
        }

        db.Subscriptions.Add(Subscription.Create(currentUser.Id, channelId, dateTimeProvider.DateTimeNow));

        var existingChannel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (existingChannel is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return existingChannel;
        }

        var effectiveIsArchived = currentUser.IsAdmin && (isArchived ?? false);
        var channel = Channel.Create(channelId, login, effectiveQualityRank, effectiveIsArchived);

        db.Channels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);

        if (effectiveIsArchived)
            return channel;

        await twitchSubscription.AddChannelsAsync([channel], cancellationToken);
        await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
        return channel;
    }

    public async Task<Result> UnsubscribeChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var subscription = await db.Subscriptions
            .Include(uc => uc.Channel)
            .FirstOrDefaultAsync(uc => uc.UserId == currentUser.Id && uc.ChannelId == channelId, cancellationToken);

        if (subscription is null)
            return Error.NotFound($"Channel '{channelId}' was not found for user.");

        db.Subscriptions.Remove(subscription);

        var otherSubscribers = await db.Subscriptions
            .CountAsync(uc => uc.ChannelId == channelId && uc.UserId != currentUser.Id, cancellationToken);

        if (otherSubscribers == 0)
            await TeardownAsync(subscription.Channel, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    public async Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Error.NotFound($"Channel '{channelId}' was not found.");

        await TeardownAsync(channel, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    public async Task<Result> SetArchiveStatusAsync(
        string channelId,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Error.NotFound($"Channel '{channelId}' was not found.");

        if (channel.IsArchived == isArchived)
            return Error.Validation($"Recording status is already set to {isArchived}.");

        channel.SetArchivingStatus(isArchived);
        await db.SaveChangesAsync(cancellationToken);

        if (isArchived)
            await twitchSubscription.RemoveChannelAsync(channel, cancellationToken);
        else
            await twitchSubscription.AddChannelsAsync([channel], cancellationToken);

        return Result.Success;
    }

    public async Task<Result<Channel>> UpdateChannelQualityAsync(
        string channelId,
        int qualityRank,
        CancellationToken cancellationToken = default)
    {
        var qualityRankResult = ValidateQualityRank(qualityRank);
        if (qualityRankResult.IsFailure)
            return qualityRankResult.Error;

        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Error.NotFound($"Channel '{channelId}' was not found.");

        channel.UpdateQualityRank(qualityRank);
        await db.SaveChangesAsync(cancellationToken);
        return channel;
    }

    public async Task<PagedResponse<ChannelResponse>> GetChannelsForCurrentUserAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default)
    {
        var query = db.Subscriptions
            .AsNoTracking()
            .ForUser(currentUser.Id);

        if (!currentUser.IsAdmin)
            query = query.Where(uc => !db.BannedChannels.Any(b => b.Id == uc.ChannelId));

        return await query
            .Select(uc => uc.Channel)
            .OrderByDescending(c => c.Name)
            .Select(ChannelResponse.Projection)
            .ToPagedResponseAsync(pagination);
    }

    public async Task<PagedResponse<ChannelResponse>> GetAllChannelsAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default) =>
        await db.Channels
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(ChannelResponse.Projection)
            .ToPagedResponseAsync(pagination, cancellationToken);

    private async Task TeardownAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (channel.IsLive)
            await recordingOrchestrator.StopRecordingAsync(channel.Id);

        db.Channels.Remove(channel);

        await twitchSubscription.RemoveChannelAsync(channel, cancellationToken);
        await IOUtils.DeleteDirectoryAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
    }

    private Result ValidateQualityRank(int qualityRank)
    {
        var maxQualityRank = vaultOptions.CurrentValue.MaxQualityRank;
        return qualityRank < 0 || qualityRank > maxQualityRank
            ? Error.Validation($"Quality rank must be between 0 and {maxQualityRank}.")
            : Result.Success;
    }
}