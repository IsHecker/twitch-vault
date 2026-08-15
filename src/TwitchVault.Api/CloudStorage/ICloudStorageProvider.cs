using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public interface ICloudStorageProvider
{
    StorageInstanceOptions Options { get; }

    Task<Result<IEnumerable<RemoteUrl>>> UploadAsync(
        IReadOnlyList<StorageFile> files,
        CancellationToken cancellationToken);

    Task<Result> DeleteAsync(IReadOnlyList<string> remoteUrls, CancellationToken cancellationToken);
}