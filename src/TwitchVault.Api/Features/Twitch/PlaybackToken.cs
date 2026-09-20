using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Twitch;

public readonly record struct PlaybackToken(
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("value")] string Token);