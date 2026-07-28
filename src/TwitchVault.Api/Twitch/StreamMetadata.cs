namespace TwitchVault.Api.Twitch;

public record struct StreamMetadata(string TwitchStreamId, string Title, string CategoryId, DateTime StartedAt);