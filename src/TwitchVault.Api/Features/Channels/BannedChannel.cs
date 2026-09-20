namespace TwitchVault.Api.Features.Channels;

public class BannedChannel : Entity<string>
{
    public string ChannelName { get; private set; } = string.Empty;
    public DateTime BannedAt { get; private set; }
    public string? Reason { get; private set; }

    private BannedChannel() { }

    public static BannedChannel Create(string channelId, string channelName, DateTime bannedAt, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        return new BannedChannel
        {
            Id = channelId.Trim(),
            ChannelName = channelName.Trim(),
            BannedAt = bannedAt,
            CreatedAt = bannedAt,
            Reason = reason?.Trim()
        };
    }
}