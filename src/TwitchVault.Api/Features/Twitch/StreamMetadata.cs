namespace TwitchVault.Api.Features.Twitch;

public record struct StreamMetadata(string Id, string Title, string CategoryId, DateTime StartedAt);