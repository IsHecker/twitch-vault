using System.Text.Json.Serialization;

namespace TwitchVault.Api.StoppedStreamCheck;

public sealed class StoppedStreamCheckOptions
{
    public const string SectionName = "StoppedStreamCheck";

    public bool Enabled { get; set; }
    public float RunIntervalInMinutes { get; set; }

    [JsonIgnore]
    public TimeSpan RunInterval => TimeSpan.FromMinutes(RunIntervalInMinutes);
}