namespace TwitchVault.Api.Twitch;

public record struct StreamMetadata(
    string TwitchStreamId,
    string ChannelName,
    string PreviewImageUrl,
    string Title,
    string GameDisplayName);