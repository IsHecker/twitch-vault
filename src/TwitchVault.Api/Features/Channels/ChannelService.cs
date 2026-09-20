using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Extensions;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Channels;

public class ChannelService(
    AppDbContext db,
    ITwitchGqlClient twitchGqlClient,
    ITwitchSubscriptionService twitchSubscription,
    IRecordingOrchestrator recordingOrchestrator,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    IOptionsMonitor<VaultOptions> vaultOptions) : IChannelService
{
    public async Task<Result<Channel>> AddChannelAsync(
        Guid userId,
        string channelName,
        int? qualityRank,
        bool? isArchived,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var channelId = await twitchGqlClient.GetChannelIdAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound("Channel doesn't exist on Twitch.");

        var isBanned = await db.BannedChannels.AnyAsync(b => b.Id == channelId, cancellationToken);
        if (isBanned && !isAdmin)
            return Error.Forbidden("This channel has been banned by an administrator.");

        var alreadyMonitoring = await db.UserChannels
            .AnyAsync(uc => uc.UserId == userId && uc.ChannelId == channelId, cancellationToken);

        if (alreadyMonitoring)
            return Error.Conflict("You are already subscribed to this channel.");

        var currentSubscriptions = await db.UserChannels
            .CountAsync(uc => uc.UserId == userId, cancellationToken);

        var limit = vaultOptions.CurrentValue.MaxSubscriptionsPerUser;
        if (currentSubscriptions >= limit)
            return Error.Validation($"Subscription limit reached. You cannot subscribe to more than {limit} channels.");

        db.UserChannels.Add(UserChannel.Create(userId, channelId, dateTimeProvider.DateTimeNow));

        var existingChannel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (existingChannel is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return existingChannel;
        }

        var effectiveQualityRank = isAdmin && qualityRank.HasValue ? qualityRank.Value : 2;
        var effectiveIsArchived = isAdmin && (isArchived ?? false);

        var channel = Channel.Create(channelId, channelName, effectiveQualityRank, effectiveIsArchived);

        db.Channels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);

        if (effectiveIsArchived)
            return channel;

        await twitchSubscription.AddChannelsAsync([channel], cancellationToken);
        await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
        return channel;
    }

    public async Task<Result> UnsubscribeChannelAsync(Guid userId, string channelId, CancellationToken cancellationToken = default)
    {
        var userChannel = await db.UserChannels
            .Include(uc => uc.Channel)
            .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChannelId == channelId, cancellationToken);

        if (userChannel is null)
            return Error.NotFound($"Channel '{channelId}' was not found for user.");

        db.UserChannels.Remove(userChannel);
        var remainingUserCount = await db.UserChannels.CountAsync(uc => uc.ChannelId == channelId, cancellationToken);

        if (remainingUserCount - 1 > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }

        if (userChannel.Channel.IsLive)
        {
            await recordingOrchestrator.StopRecordingAsync(channelId);
        }

        db.Channels.Remove(userChannel.Channel);
        await IOUtils.DeleteDirectoryAsync(Path.Combine(pathsOptions.Value.Streams, userChannel.Channel.Name));
        await twitchSubscription.RemoveChannelAsync(userChannel.Channel, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    public async Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = await db.Channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
            return Error.NotFound($"Channel '{channelId}' was not found.");

        if (channel.IsLive)
        {
            await recordingOrchestrator.StopRecordingAsync(channel.Id);
        }

        var userChannels = await db.UserChannels.Where(uc => uc.ChannelId == channelId).ToListAsync(cancellationToken);
        db.UserChannels.RemoveRange(userChannels);
        db.Channels.Remove(channel);

        await IOUtils.DeleteDirectoryAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
        await twitchSubscription.RemoveChannelAsync(channel, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    public async Task<Result> SetArchiveStatusAsync(string channelId, bool isArchived, CancellationToken cancellationToken = default)
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

        var query = db.UserChannels
            .AsNoTracking()
            .ForUser(userId);

        if (!user.IsAdmin)
            query = query.Where(uc => !db.BannedChannels.Any(b => b.Id == uc.ChannelId));

        return await query
            .Select(u => u.Channel)
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
            .ToPagedResponseAsync(pagination);

    public async Task<Result<BannedChannel>> BanChannelAsync(
        string channelIdentifier,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelIdentifier))
            return Error.Validation("Channel identifier cannot be empty.");

        string? channelId = null;
        string? channelName = null;

        var existingChannel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelIdentifier || c.Name.ToLower() == channelIdentifier.ToLower(), cancellationToken);

        if (existingChannel is not null)
        {
            channelId = existingChannel.Id;
            channelName = existingChannel.Name;
        }
        else
        {
            channelId = await twitchGqlClient.GetChannelIdAsync(channelIdentifier, cancellationToken);
            if (string.IsNullOrWhiteSpace(channelId))
                return Error.NotFound("Channel doesn't exist on Twitch.");

            channelName = channelIdentifier;
        }

        var isAlreadyBanned = await db.BannedChannels.AnyAsync(b => b.Id == channelId, cancellationToken);
        if (isAlreadyBanned)
            return Error.Conflict($"Channel '{channelName}' is already banned.");

        var bannedChannel = BannedChannel.Create(channelId, channelName, dateTimeProvider.DateTimeNow, reason);
        db.BannedChannels.Add(bannedChannel);

        var nonAdminUserChannels = await db.UserChannels
            .Include(uc => uc.User)
            .Where(uc => uc.ChannelId == channelId && !uc.User.IsAdmin)
            .ToListAsync(cancellationToken);

        if (nonAdminUserChannels.Count > 0)
        {
            db.UserChannels.RemoveRange(nonAdminUserChannels);
        }

        var hasAdminSubscribed = await db.UserChannels
            .Include(uc => uc.User)
            .AnyAsync(uc => uc.ChannelId == channelId && uc.User.IsAdmin, cancellationToken);

        if (!hasAdminSubscribed && existingChannel is not null)
        {
            if (existingChannel.IsLive)
            {
                await recordingOrchestrator.StopRecordingAsync(channelId);
            }
            await twitchSubscription.RemoveChannelAsync(existingChannel, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return bannedChannel;
    }

    public async Task<Result> UnbanChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var bannedChannel = await db.BannedChannels
            .FirstOrDefaultAsync(b => b.Id == channelId || b.ChannelName.ToLower() == channelId.ToLower(), cancellationToken);

        if (bannedChannel is null)
            return Error.NotFound($"Banned channel '{channelId}' was not found.");

        db.BannedChannels.Remove(bannedChannel);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    public async Task<PagedResponse<BannedChannelResponse>> GetBannedChannelsAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default) =>
        await db.BannedChannels
            .AsNoTracking()
            .OrderByDescending(b => b.BannedAt)
            .Select(BannedChannelResponse.Projection)
            .ToPagedResponseAsync(pagination);
}