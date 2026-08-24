namespace TwitchVault.Api.CloudStorage;

public class StorageOptions
{
    public const string SectionName = "Storage";
    public List<StorageInstanceOptions> Instances { get; init; } = [];
}

public class StorageInstanceOptions
{
    public string Name { get; init; } = string.Empty;
    public CloudProviderType Provider { get; init; }
    public bool Enabled { get; init; }
    public StorageCredentialsOptions Credentials { get; init; } = null!;
    public StorageRateLimitOptions? RateLimit { get; init; } = null;
    public StorageBehaviorOptions Behavior { get; init; } = null!;
}

public class StorageRateLimitOptions
{
    public int MaxRetryAttempts { get; init; }
    public int BaseDelaySeconds { get; init; }
    public int PermitLimit { get; init; }
    public int WindowSeconds { get; init; }
    public int QueueLimit { get; init; }
    public int RequestTimeoutSeconds { get; init; }
}

public class StorageCredentialsOptions
{
    public string? ClientId { get; init; }
    public string? AccessToken { get; init; }
    public string? ClientSecret { get; init; }
}

public class StorageBehaviorOptions
{
    public long? StorageCapacityBytes { get; init; }
    public int MaxUploadBatchSize { get; init; }
    public int MaxDeleteBatchSize { get; init; }
    public long MaxFileSizeBytes { get; init; }
    public int QueueLimit { get; init; }
    public int MaxConcurrentUploads { get; init; }
    public int RequestTimeoutSeconds { get; init; }
}