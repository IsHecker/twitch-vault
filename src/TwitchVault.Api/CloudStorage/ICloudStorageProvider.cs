using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public interface ICloudStorageProvider
{
    StorageInstanceOptions Options { get; }

    Task<Result<IEnumerable<RemoteUrl>>> UploadAsync(
        IEnumerable<StorageFile> files,
        CancellationToken cancellationToken);

    Task<Result> DeleteAsync(IEnumerable<string> remoteUrls, CancellationToken cancellationToken);
}