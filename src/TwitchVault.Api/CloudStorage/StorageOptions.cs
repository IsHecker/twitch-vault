using TwitchVault.Api.CloudStorage.Providers;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageInstanceConfig
{
    public string Id { get; init; } = null!;
    public CloudProviderType Type { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string DefaultInstanceId { get; init; } = null!;
    public List<string> EnabledInstances { get; init; } = [];
    public List<StorageInstanceConfig> Instances { get; init; } = [];
}