using System.Linq.Expressions;

namespace TwitchVault.Api.Features.Channels.Endpoints;

public record BannedChannelResponse(
    string Id,
    string ChannelName,
    DateTime BannedAt,
    string? Reason)
{
    public static Expression<Func<BannedChannel, BannedChannelResponse>> Projection =>
        b => new BannedChannelResponse(
            b.Id,
            b.ChannelName,
            b.BannedAt,
            b.Reason);

    public static BannedChannelResponse FromDomain(BannedChannel bannedChannel) =>
        new(
            bannedChannel.Id,
            bannedChannel.ChannelName,
            bannedChannel.BannedAt,
            bannedChannel.Reason);
}