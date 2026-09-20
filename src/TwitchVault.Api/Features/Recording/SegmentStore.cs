using TwitchVault.Api.Configuration;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Features.Recording;

public readonly record struct LocalSegment(string FilePath, float Duration, long SizeBytes);

public interface ISegmentStore
{
    Task<LocalSegment?> SaveAsync(
        string streamFolderRelativePath,
        SegmentContent segment,
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
    private System.IO.Stream? _currentFileStream;

    public bool IsFull => _accumulatedDuration >= vaultOptions.CurrentValue.MaxSegmentDurationInSec;

    public async Task<LocalSegment?> SaveAsync(
        string streamFolderPath,
        SegmentContent segment,
        string? lastSegmentFileName,
        CancellationToken cancellationToken)
    {
        if (segment.Source.IsInitSegment)
            return await SaveInitSegmentAsync(streamFolderPath, segment, cancellationToken);

        return await SaveSegmentAsync(streamFolderPath, segment, lastSegmentFileName, cancellationToken);
    }

    public LocalSegment? CloseCurrentSegment()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return null;

        var sizeBytes = _currentFileStream?.Length ?? 0;
        _currentFileStream?.Flush();
        _currentFileStream?.Dispose();
        _currentFileStream = null;

        var closed = new LocalSegment(_currentFilePath, _accumulatedDuration, sizeBytes);

        _currentFileName = null;
        _currentFilePath = null;
        _accumulatedDuration = 0f;
        return closed;
    }

    private async Task<LocalSegment> SaveInitSegmentAsync(
        string streamFolderPath,
        SegmentContent segment,
        CancellationToken cancellationToken)
    {
        var content = segment.ResponseStream.Content;
        var initFileName = $"init{GetUrlExtension(segment.Source.Url)}";
        var initPath = Path.Combine(streamFolderPath, initFileName);

        await using var fileStream = fileSystem.OpenWrite(initPath, FileMode.Create);
        await content.CopyToAsync(fileStream, cancellationToken);

        return new LocalSegment(initPath, 0, fileStream.Length);
    }

    private async Task<LocalSegment?> SaveSegmentAsync(
        string streamFolderPath,
        SegmentContent segment,
        string? lastFlushedFileName,
        CancellationToken cancellationToken)
    {
        var content = segment.ResponseStream.Content;
        EnsureCurrentFileStream(streamFolderPath, lastFlushedFileName, GetUrlExtension(segment.Source.Url));
        await content.CopyToAsync(_currentFileStream!, cancellationToken);

        _accumulatedDuration += segment.Source.Duration;
        return !IsFull ? null : CloseCurrentSegment();
    }

    private void EnsureCurrentFileStream(string streamFolderPath, string? lastFlushedFileName, string urlExtension)
    {
        if (_currentFileStream is not null)
            return;

        var fileName = _currentFileName ?? StartNewSegment(lastFlushedFileName, urlExtension);
        _currentFilePath = Path.Combine(streamFolderPath, fileName);
        _currentFileStream = fileSystem.OpenWrite(_currentFilePath, FileMode.Create);
    }

    private string StartNewSegment(string? lastFlushedFileName, string urlExtension)
    {
        var nextIndex = HlsSegmentNaming.GetSegmentIndex(lastFlushedFileName) + 1;
        _currentFileName = HlsSegmentNaming.FormatSegmentFileName(nextIndex, urlExtension);
        _accumulatedDuration = 0f;
        return _currentFileName;
    }

    private static string GetUrlExtension(string url)
    {
        var span = url.AsSpan();
        var queryIndex = span.IndexOf('?');
        if (queryIndex >= 0)
            span = span[..queryIndex];

        var dotIndex = span.LastIndexOf('.');
        var slashIndex = span.LastIndexOf('/');
        if (dotIndex <= slashIndex || dotIndex < 0)
            return ".ts";

        var ext = span[dotIndex..];
        if (ext.Equals(".ts", StringComparison.OrdinalIgnoreCase))
            return ".ts";

        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return ".mp4";

        if (ext.Equals(".m4s", StringComparison.OrdinalIgnoreCase))
            return ".m4s";

        return ext.ToString();
    }
}