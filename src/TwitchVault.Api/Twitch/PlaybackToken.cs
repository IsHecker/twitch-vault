using System.Text.Json.Serialization;
using TwitchVault.Api.Recording.HLS;

using TwitchVault.Api.Events;
namespace TwitchVault.Api.Twitch;


public readonly record struct PlaybackToken(
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("value")] string Token);
