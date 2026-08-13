using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public interface ICloudStorageProvider
{
    StorageInstanceOptions Options { get; }

    // Task<Result> UploadAsync(Stream data, LocalSegment segment, CancellationToken cancellationToken);

    Task<Result<IEnumerable<string>>> UploadAsync(
        IEnumerable<Stream> dataStreams,
        IEnumerable<LocalSegment> segments,
        CancellationToken cancellationToken);

    Task<Result> DeleteBatchAsync(Domain.Stream stream, string playlistContent, CancellationToken cancellationToken);
}