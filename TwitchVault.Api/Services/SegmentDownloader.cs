using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public class SegmentDownloader(TwitchClient twitchClient)
{
    private const string SegmentPrefix = "seg_";

    private float _currentSegmentDuration = 0;
    private string? _currentFileName = null;

    private PlaylistBuilder _playlistBuilder = null!;

    public void SetPlaylist(PlaylistBuilder playlistBuilder) => _playlistBuilder = playlistBuilder;

    public async Task DownloadSegmentsAsync(
        IEnumerable<Segment> segments,
        string streamFolderPath,
        int maxSegmentDuration,
        CancellationToken cancellationToken)
    {
        FileStream? fileStream = null;

        try
        {
            foreach (var segment in segments)
            {
                if (segment.IsInit)
                {
                    await DownloadInitSegmentAsync(segment.Url, streamFolderPath, _playlistBuilder, cancellationToken);
                    continue;
                }

                if (fileStream is null)
                {
                    var index = GetNextSegmentNumber(_playlistBuilder.LastSegmentFileName);
                    _currentFileName = $"{SegmentPrefix}{index}{GetUrlExtension(segment.Url)}";
                    var segmentPath = Path.Combine(streamFolderPath, _currentFileName);

                    fileStream = OpenSegmentFile(segmentPath, FileMode.Append);
                }

                await using var segmentStream = await twitchClient.DownloadAsStreamAsync(segment.Url, cancellationToken);
                await segmentStream.CopyToAsync(fileStream, cancellationToken);

                _currentSegmentDuration += segment.DurationSeconds;

                if (_currentSegmentDuration < maxSegmentDuration)
                    continue;

                _playlistBuilder.AddSegment(_currentFileName!, _currentSegmentDuration);

                fileStream?.Dispose();

                fileStream = null;
                _currentSegmentDuration = 0f;
                _currentFileName = null;
            }
        }
        finally
        {
            fileStream?.Dispose();
        }
    }

    public void FlushCurrentSegment()
    {
        if (_currentFileName is null || _currentSegmentDuration <= 0)
            return;

        _playlistBuilder.AddSegment(_currentFileName, _currentSegmentDuration);
        _currentSegmentDuration = 0f;
        _currentFileName = null;
    }

    private async Task DownloadInitSegmentAsync(
        string url,
        string streamFolderPath,
        PlaylistBuilder playlistBuilder,
        CancellationToken cancellationToken)
    {
        if (playlistBuilder.IsInitSegmentSet)
            return;

        var initFileName = $"init{GetUrlExtension(url)}";
        var initPath = Path.Combine(streamFolderPath, initFileName);

        await using var initStream = await twitchClient.DownloadAsStreamAsync(url, cancellationToken);
        await using var fileStream = OpenSegmentFile(initPath, FileMode.Create);
        await initStream.CopyToAsync(fileStream, cancellationToken);

        playlistBuilder.AddMap(initFileName);
    }

    private static FileStream OpenSegmentFile(string path, FileMode mode) =>
        new(path, mode, FileAccess.Write, FileShare.Read, bufferSize: 8192, useAsync: true);

    private static string GetUrlExtension(string url) =>
        Path.GetExtension(new Uri(url).AbsolutePath);

    private static int GetNextSegmentNumber(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return 1;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        return int.Parse(nameWithoutExtension[SegmentPrefix.Length..]) + 1;
    }
}