using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Channels.Jobs;

public sealed class ChannelMonitorOptions
{
    public bool Enabled { get; set; }
    public float RunIntervalInMinutes { get; set; }

    [JsonIgnore]
    public TimeSpan RunInterval => TimeSpan.FromMinutes(RunIntervalInMinutes);
}