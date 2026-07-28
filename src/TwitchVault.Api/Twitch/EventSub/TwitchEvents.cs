namespace TwitchVault.Api.Twitch.EventSub;

public readonly record struct ChannelUpdateEvent(string ChannelId, string Title, string CategoryId)
{
    public static readonly string EventName = "channel.update";
};