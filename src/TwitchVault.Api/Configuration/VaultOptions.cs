namespace TwitchVault.Api.Configuration;

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public int MaxSegmentDurationInSec { get; set; }
    public int MaxConsecutiveEmptyPolls { get; set; }
    public int UploadBatchSize { get; set; }
    public int MaxConcurrentUploadWorkers { get; set; }
    public int IdleFlushTimeoutSeconds { get; set; }
    public int MaxSubscriptionsPerUser { get; set; }
    public int MaxQualityRank { get; set; }
    public int DefaultQualityRank { get; set; }
}