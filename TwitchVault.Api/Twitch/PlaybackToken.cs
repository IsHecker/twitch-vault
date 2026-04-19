using System.Text.Json.Serialization;

namespace TwitchVault.Api.Twitch;

public readonly record struct PlaybackToken(
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("value")] string Token);