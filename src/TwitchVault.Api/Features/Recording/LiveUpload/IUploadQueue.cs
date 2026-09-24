namespace TwitchVault.Api.Features.Recording.LiveUpload;

public readonly record struct UploadBatch(
    Streams.Stream Stream,
    string[] LocalFilePaths,
    Action? OnCompleted = null,
    bool IsUrgent = false);

public interface IUploadQueue
{
    ValueTask QueueBatchAsync(UploadBatch batch, CancellationToken cancellationToken = default);
    IAsyncEnumerable<UploadBatch> ReadAllAsync(CancellationToken cancellationToken = default);
    void Complete();
}