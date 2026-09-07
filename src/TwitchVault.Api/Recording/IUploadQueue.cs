namespace TwitchVault.Api.Recording;

public record UploadBatch(
    Domain.Stream Stream,
    IReadOnlyList<string> LocalFilePaths,
    Action? OnCompleted = null,
    bool IsUrgent = false);

public interface IUploadQueue
{
    ValueTask QueueBatchAsync(UploadBatch batch, CancellationToken cancellationToken = default);
    IAsyncEnumerable<UploadBatch> ReadAllAsync(CancellationToken cancellationToken = default);
}