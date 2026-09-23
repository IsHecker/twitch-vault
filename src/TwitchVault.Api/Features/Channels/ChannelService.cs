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
    public async Task<Result<Channel>> SubscribeToChannelAsync(
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

        var bannedChannel = await db.BannedChannels.FirstOrDefaultAsync(b => b.ChannelName == login, cancellationToken);
        if (bannedChannel is not null && !currentUser.IsAdmin)
            return Error.Validation($"This channel has been banned. Reason: '{bannedChannel.Reason}'");

        var channelId = await twitchGqlClient.GetChannelIdAsync(login, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound("Channel doesn't exist on Twitch.");

        var alreadySubscribed = await db.Subscriptions
            .AnyAsync(s => s.UserId == currentUser.Id && s.ChannelId == channelId, cancellationToken);

        if (alreadySubscribed)
            return Error.Conflict("You are already subscribed to this channel.");

        if (!currentUser.IsAdmin)
        {
            var currentSubscriptions = await db.Subscriptions
                .CountAsync(s => s.UserId == currentUser.Id, cancellationToken);

            if (currentSubscriptions >= options.MaxSubscriptionsPerUser)
                return Error.Validation(
                    $"Subscription limit reached. You cannot subscribe to more than {options.MaxSubscriptionsPerUser} channels.");
        }

        db.Subscriptions.Add(Subscription.Create(currentUser.Id, channelId, dateTimeProvider.DateTimeNow));

        var (channel, isNew) = await GetOrCreateChannelAsync(
            channelId, login, effectiveQualityRank, isArchived, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (isNew && !channel.IsArchived)
        {
            await twitchSubscription.AddChannelsAsync([channel], cancellationToken);
            await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
        }

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
            await DeleteChannelAsync(subscription.Channel, cancellationToken);

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

    public async Task<Result<PagedResponse<ChannelResponse>>> GetChannelsForUserAsync(
        Guid userId,
        Pagination pagination,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound($"User '{userId}' not found.");

        var query = db.Subscriptions
            .AsNoTracking()
            .ForUser(userId);

        if (!user.IsAdmin)
            query = query.Where(uc => !db.BannedChannels.Any(b => b.Id == uc.ChannelId));

        return await query
            .Select(uc => uc.Channel)
            .OrderByDescending(c => c.Name)
            .Select(ChannelResponse.Projection)
            .ToPagedResponseAsync(pagination, cancellationToken);
    }

    public async Task<PagedResponse<ChannelResponse>> GetAllChannelsAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default) =>
        await db.Channels
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(ChannelResponse.Projection)
            .ToPagedResponseAsync(pagination, cancellationToken);

    private async Task<(Channel Channel, bool IsNew)> GetOrCreateChannelAsync(
        string channelId,
        string login,
        int qualityRank,
        bool? isArchived,
        CancellationToken cancellationToken)
    {
        var existingChannel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (existingChannel is not null)
            return (existingChannel, false);

        var effectiveIsArchived = currentUser.IsAdmin && (isArchived ?? false);
        var channel = Channel.Create(channelId, login, qualityRank, effectiveIsArchived);

        db.Channels.Add(channel);

        return (channel, true);
    }

    private async Task DeleteChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (channel.IsLive)
            await recordingOrchestrator.StopRecordingAsync(channel.Id);

        await db.Streams
            .Where(s => s.ChannelId == channel.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.StorageOperationStatus, StorageOperationStatus.DeleteRequest),
                cancellationToken);

        db.Channels.Remove(channel);
        await twitchSubscription.RemoveChannelAsync(channel, cancellationToken);

        var channelDir = Path.Combine(pathsOptions.Value.Streams, channel.Name);
        if (Directory.Exists(channelDir) && !Directory.EnumerateFileSystemEntries(channelDir).Any())
        {
            await IOUtils.DeleteDirectoryAsync(channelDir);
        }
    }

    private Result ValidateQualityRank(int qualityRank)
    {
        var maxQualityRank = vaultOptions.CurrentValue.MaxQualityRank;
        return qualityRank < 0 || qualityRank > maxQualityRank
            ? Error.Validation($"Quality rank must be between 0 and {maxQualityRank}.")
            : Result.Success;
    }
}