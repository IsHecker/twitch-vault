namespace TwitchVault.Api.Features.Twitch.EventSub;

public readonly record struct ChannelUpdateEvent(string ChannelId, string Title, string CategoryId);