using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Twitch.EventSub;

public sealed class TwitchWebhookHealthCheckOptions
{
    public bool Enabled { get; set; }
    public float RunIntervalInMinutes { get; set; }

    [JsonIgnore]
    public TimeSpan RunInterval => TimeSpan.FromMinutes(RunIntervalInMinutes);
}