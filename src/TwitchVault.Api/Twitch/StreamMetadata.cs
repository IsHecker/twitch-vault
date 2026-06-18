namespace TwitchVault.Api.Twitch;

public record struct StreamMetadata(string TwitchStreamId, string PreviewImageUrl, string Title, string CategoryName);