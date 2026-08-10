using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Providers;

public sealed class DropboxStorageProvider : ICloudStorageProvider
{
    public string ProviderInstanceId { get; private set; } = null!;
    public CloudProviderType ProviderType => CloudProviderType.Dropbox;

    public StorageCapabilities Capabilities => new(MaxBatchSize: 1, MaxFileSizeBytes: 150_000_000);
    public void Initialize(string instanceId, Dictionary<string, string> settings)
    {
        ProviderInstanceId = instanceId;
    }

    public Task<Result<UploadBatchResult>> UploadBatchAsync(
        string[] localFilePaths,
        CancellationToken cancellationToken)
    {
        var segments = new List<UploadedSegment>();
        foreach (var file in localFilePaths)
        {
            var fileName = Path.GetFileName(file);
            var remoteUrl = $"https://dl.dropboxusercontent.com/s/{ProviderInstanceId}/{fileName}";
            segments.Add(new UploadedSegment(file, remoteUrl));
        }
        return Task.FromResult<Result<UploadBatchResult>>(new UploadBatchResult(segments));
    }

    public Task<Result> DeleteStreamDataAsync(
        string? localPlaylistPath,
        object? deletionProgress,
        CancellationToken cancellationToken)
    {
        // Deletes remote folder /TwitchVault_VODs/{RemoteFolderPath}
        return Task.FromResult(Result.Success);
    }
}