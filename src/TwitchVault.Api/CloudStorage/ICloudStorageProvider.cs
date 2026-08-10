using TwitchVault.Api.CloudStorage.Providers;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public interface ICloudStorageProvider
{
    CloudProviderType ProviderType { get; }
    string ProviderInstanceId { get; }
    StorageCapabilities Capabilities { get; }

    void Initialize(string instanceId, Dictionary<string, string> settings);

    Task<Result<UploadBatchResult>> UploadBatchAsync(
        string[] localFilePaths,
        CancellationToken cancellationToken);

    Task<Result> DeleteStreamDataAsync(
        string? localPlaylistPath,
        object? deletionProgress,
        CancellationToken cancellationToken);
}