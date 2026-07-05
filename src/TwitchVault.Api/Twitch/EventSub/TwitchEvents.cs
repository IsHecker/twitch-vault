namespace TwitchVault.Api.Events;

public readonly record struct ChannelUpdateEvent(string ChannelId, string Title, string CategoryName) { public static readonly string EventName = "channel.update"; };
