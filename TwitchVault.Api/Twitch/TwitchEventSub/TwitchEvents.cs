namespace TwitchVault.Api.Twitch.TwitchEventSub;

public readonly record struct StreamOnlineEvent(string ChannelId, string ChannelName, DateTime StartedAt)
{
    public static readonly string EventName = "stream.online";
};

public readonly record struct ChannelUpdateEvent(string ChannelId, string Title, string CategoryName)
{
    public static readonly string EventName = "channel.update";
};