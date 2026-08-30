using TwitchVault.Api.Configuration;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Recording;

public interface ISegmentUploader : IDisposable
{
    void Attach(Domain.Stream stream);
    void Add(LocalSegment segment);
    Task FlushRemainingAsync();
}

public sealed class LiveSegmentUploader(
    IStreamStorageService storageService,
    IWebHostEnvironment env,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<LiveSegmentUploader> logger) : ISegmentUploader
{
    private readonly List<LocalSegment> _buffer = [];
    private Task _pendingFlush = Task.CompletedTask;
    private Domain.Stream _stream = null!;
    private string _localDirectory = null!;
    private string _remoteUrlsFilePath = null!;
    private StreamWriter _remoteUrlsWriter = null!;

    public void Attach(Domain.Stream stream)
    {
        _stream = stream;

        _localDirectory = _stream.Folder.GetAbsolutePath(env.ContentRootPath);
        _remoteUrlsFilePath = Path.Combine(_localDirectory, IStreamStorageService.RemoteUrlsFileName);
        _remoteUrlsWriter = new StreamWriter(_remoteUrlsFilePath, append: true);
    }

    public void Add(LocalSegment segment)
    {
        lock (_buffer)
        {
            _buffer.Add(segment);
            if (_buffer.Count < vaultOptions.CurrentValue.UploadBatchSize)
                return;
        }

        StartFlush();
    }

    public async Task FlushRemainingAsync()
    {
        lock (_buffer)
        {
            if (_buffer.Count > 0)
                StartFlush();
        }

        await _pendingFlush;
    }

    private void StartFlush()
    {
        var previous = _pendingFlush;
        _pendingFlush = FlushAsync(previous);
    }

    private async Task FlushAsync(Task previousFlush)
    {
        await previousFlush.ContinueWith(_ => { });

        List<string> batch;
        lock (_buffer)
        {
            batch = _buffer.Select(seg => seg.FilePath).ToList();
        }

        if (batch.Count == 0)
            return;

        var succeeded = await storageService.UploadBatchAsync(
            batch,
            _stream,
            _remoteUrlsWriter,
            CancellationToken.None);

        if (!succeeded)
        {
            logger.LogWarning(
                "Batch upload failed for stream '{StreamId}'. " +
                "{Count} segment(s) will be retried on the next flush.",
                _stream.Id, batch.Count);
            return;
        }

        lock (_buffer)
        {
            _buffer.RemoveRange(0, batch.Count);
        }
    }

    public void Dispose()
    {
        _remoteUrlsWriter.Flush();
        _remoteUrlsWriter.Dispose();
    }
}