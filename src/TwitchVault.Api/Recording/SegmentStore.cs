using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording.HLS;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Recording;

public sealed record LocalSegment(string FilePath, float Duration, long SizeBytes);

public interface ISegmentStore
{
    Task<LocalSegment?> SaveAsync(
        string streamFolderRelativePath,
        DownloadedSegment segment,
        string? lastSegmentFileName,
        CancellationToken cancellationToken);

    LocalSegment? CloseCurrentSegment();
}

public sealed class SegmentStore(IFileSystem fileSystem, IOptionsMonitor<VaultOptions> vaultOptions) : ISegmentStore
{
    // TODO: no need for sequential segment name, i can just use guid.
    private float _accumulatedDuration;
    private string? _currentFileName;
    private string? _currentFilePath;
    private long _fileSizeBytes;
    public bool IsFull => _accumulatedDuration >= vaultOptions.CurrentValue.MaxSegmentDurationInSec;

    public async Task<LocalSegment?> SaveAsync(
        string streamFolderPath,
        DownloadedSegment segment,
        string? lastSegmentFileName,
        CancellationToken cancellationToken)
    {
        if (segment.Source.IsInitSegment)
            return await SaveInitSegmentAsync(streamFolderPath, segment, cancellationToken);

        return await SaveSegmentAsync(streamFolderPath, segment, lastSegmentFileName, cancellationToken);
    }

    private async Task<LocalSegment?> SaveSegmentAsync(
        string streamFolderPath,
        DownloadedSegment segment,
        string? lastSegmentFileName,
        CancellationToken cancellationToken)
    {
        string fileName = GetSegmentFilePath(lastSegmentFileName, GetUrlExtension(segment.Source.Url));
        _currentFilePath = Path.Combine(streamFolderPath, fileName);

        await using var fileStream = fileSystem.OpenWrite(_currentFilePath, FileMode.Append);
        await segment.Content.CopyToAsync(fileStream!, cancellationToken);
        await segment.Content.DisposeAsync();

        _accumulatedDuration += segment.Source.Duration;
        _fileSizeBytes += fileStream.Length;

        return !IsFull ? null : CloseCurrentSegment();
    }

    public LocalSegment? CloseCurrentSegment()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return null;

        var closed = new LocalSegment(_currentFilePath, _accumulatedDuration, _fileSizeBytes);
        _currentFileName = null;
        _currentFilePath = null;
        _accumulatedDuration = 0f;
        _fileSizeBytes = 0;
        return closed;
    }

    private async Task<LocalSegment> SaveInitSegmentAsync(
        string streamFolderPath,
        DownloadedSegment segment,
        CancellationToken cancellationToken)
    {
        var initFileName = $"init{GetUrlExtension(segment.Source.Url)}";
        var initPath = Path.Combine(streamFolderPath, initFileName);

        await using var fileStream = fileSystem.OpenWrite(initPath, FileMode.Create);
        await segment.Content.CopyToAsync(fileStream, cancellationToken);
        await segment.Content.DisposeAsync();

        return new LocalSegment(initPath, 0, fileStream.Length);
    }

    private static string GetUrlExtension(string url) =>
        Path.GetExtension(new Uri(url).AbsolutePath);

    private string GetSegmentFilePath(string? lastFlushedFileName, string urlExtension) =>
        _currentFileName ?? StartNewSegment(lastFlushedFileName, urlExtension);

    private string StartNewSegment(string? lastFlushedFileName, string urlExtension)
    {
        var nextIndex = HlsSegmentNaming.GetSegmentIndex(lastFlushedFileName) + 1;
        _currentFileName = HlsSegmentNaming.FormatSegmentFileName(nextIndex, urlExtension);
        _accumulatedDuration = 0f;
        return _currentFileName;
    }
}