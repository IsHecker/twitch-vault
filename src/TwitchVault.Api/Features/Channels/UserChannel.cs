namespace TwitchVault.Api.Features.Channels;

public sealed class UserChannel
{
    public Guid UserId { get; init; }
    public string ChannelId { get; init; } = null!;
    public DateTime AddedAt { get; init; }

    public Channel Channel { get; init; } = null!;
    public User User { get; init; } = null!;

    private UserChannel() { }

    public static UserChannel Create(Guid userId, string channelId, DateTime addedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        return new UserChannel
        {
            UserId = userId,
            ChannelId = channelId.Trim(),
            AddedAt = addedAt
        };
    }
}