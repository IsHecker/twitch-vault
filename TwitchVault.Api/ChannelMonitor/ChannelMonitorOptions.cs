using System.Text.Json.Serialization;

namespace TwitchVault.Api.ChannelMonitor;

public sealed class ChannelMonitorOptions
{
    public const string SectionName = "ChannelMonitor";

    public bool Enabled { get; set; }
    public float RunIntervalInMinutes { get; set; }

    [JsonIgnore]
    public TimeSpan RunInterval => TimeSpan.FromMinutes(RunIntervalInMinutes);
}