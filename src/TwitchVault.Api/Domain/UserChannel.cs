namespace TwitchVault.Api.Domain;

public sealed record UserChannel(Guid UserId, string ChannelId, DateTime AddedAt)
{
    public static UserChannel Create(Guid userId, string channelId, DateTime addedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        return new UserChannel(userId, channelId.Trim(), addedAt);
    }
}