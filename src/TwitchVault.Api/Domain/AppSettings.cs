using System.Text.Json.Serialization;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Domain;

public class AppSettings
{
    public VaultOptions Vault { get; set; } = new();
    public TwitchOptions Twitch { get; set; } = new();
    public Dictionary<string, JobOptions> BackgroundJobs { get; set; } = [];
}

public sealed class JobOptions
{
    public const string ChannelMonitor = nameof(ChannelMonitor);
    public const string TwitchWebhookHealthCheck = nameof(TwitchWebhookHealthCheck);
    public const string StorageUpload = nameof(StorageUpload);
    public const string StorageCleanup = nameof(StorageCleanup);

    public bool Enabled { get; set; }
    public float RunIntervalInMinutes { get; set; }

    [JsonIgnore]
    public TimeSpan RunInterval => TimeSpan.FromMinutes(RunIntervalInMinutes);
}