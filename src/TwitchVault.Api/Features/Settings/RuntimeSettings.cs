using System.Text.Json.Serialization;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Settings;

public class RuntimeSettings
{
    public VaultOptions Vault { get; set; } = new();
    public TwitchOptions Twitch { get; set; } = new();
    public BackgroundJobsOptions BackgroundJobs { get; set; } = [];
}

public sealed class BackgroundJobsOptions : Dictionary<string, JobOptions>
{
    public const string SectionName = "BackgroundJobs";

    public JobOptions GetJob(string jobName) =>
        TryGetValue(jobName, out var opt) ? opt : new JobOptions();
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