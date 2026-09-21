using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Extensions;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Channels;

public interface IChannelBanService
{
    Task<Result<BannedChannel>> BanChannelAsync(
        string channelName,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<Result> UnbanChannelAsync(string channelName, CancellationToken cancellationToken = default);

    Task<PagedResponse<BannedChannelResponse>> ListBannedChannelsAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default);
}

public sealed class ChannelBanService(
    AppDbContext db,
    ITwitchGqlClient twitchGqlClient,
    ITwitchSubscriptionService twitchSubscription,
    IRecordingOrchestrator recordingOrchestrator,
    IOptions<PathsOptions> pathsOptions,
    IDateTimeProvider dateTimeProvider) : IChannelBanService
{
    public async Task<Result<BannedChannel>> BanChannelAsync(
        string channelName,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(channelName, cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var (channelId, login, existingChannel) = resolved.Value;

        var isAlreadyBanned = await db.BannedChannels.AnyAsync(b => b.Id == channelId, cancellationToken);
        if (isAlreadyBanned)
            return Error.Conflict($"Channel '{login}' is already banned.");

        var bannedChannel = BannedChannel.Create(channelId, login, dateTimeProvider.DateTimeNow, reason);
        db.BannedChannels.Add(bannedChannel);

        var nonAdminSubscriptions = await db.Subscriptions
            .Where(uc => uc.ChannelId == channelId && !uc.User.IsAdmin)
            .ToListAsync(cancellationToken);

        db.Subscriptions.RemoveRange(nonAdminSubscriptions);

        var hasAdminSubscriber = await db.Subscriptions
            .AnyAsync(uc => uc.ChannelId == channelId && uc.User.IsAdmin, cancellationToken);

        if (!hasAdminSubscriber && existingChannel is not null)
            await TeardownAsync(existingChannel, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return bannedChannel;
    }

    public async Task<Result> UnbanChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        TwitchLogin.TryNormalize(channelName, out var login);

        var bannedChannel = await db.BannedChannels
            .FirstOrDefaultAsync(b => b.ChannelName == login, cancellationToken);

        if (bannedChannel is null)
            return Error.NotFound($"Banned channel '{channelName}' was not found.");

        db.BannedChannels.Remove(bannedChannel);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    public async Task<PagedResponse<BannedChannelResponse>> ListBannedChannelsAsync(
        Pagination pagination,
        CancellationToken cancellationToken = default) =>
        await db.BannedChannels
            .AsNoTracking()
            .OrderByDescending(b => b.BannedAt)
            .Select(BannedChannelResponse.Projection)
            .ToPagedResponseAsync(pagination);

    private async Task<Result<(string ChannelId, string login, Channel? ExistingChannel)>> ResolveAsync(
        string channelName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return Error.Validation("Channel name cannot be empty.");

        var isLogin = TwitchLogin.TryNormalize(channelName, out var login);
        if (!isLogin)
            return Error.Validation("Invalid Twitch channel name.");

        var existingChannel = await db.Channels
            .FirstOrDefaultAsync(c => c.Name == login, cancellationToken);

        if (existingChannel is not null)
            return (existingChannel.Id, existingChannel.Name, existingChannel);

        var channelId = await twitchGqlClient.GetChannelIdAsync(login, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound("Channel doesn't exist on Twitch.");

        return (channelId, login, null);
    }

    private async Task TeardownAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (channel.IsLive)
            await recordingOrchestrator.StopRecordingAsync(channel.Id);

        db.Channels.Remove(channel);

        await twitchSubscription.RemoveChannelAsync(channel, cancellationToken);
        await IOUtils.DeleteDirectoryAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
    }
}