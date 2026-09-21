namespace TwitchVault.Api.Features.Channels;

public sealed class Subscription
{
    public Guid UserId { get; init; }
    public string ChannelId { get; init; } = null!;
    public DateTime AddedAt { get; init; }

    public Channel Channel { get; init; } = null!;
    public User User { get; init; } = null!;

    private Subscription() { }

    public static Subscription Create(Guid userId, string channelId, DateTime addedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        return new Subscription
        {
            UserId = userId,
            ChannelId = channelId.Trim(),
            AddedAt = addedAt
        };
    }
}