using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording.HLS;

public class SegmentDownloader(ITwitchGqlClient twitchGqlClient)
{
    private const string SegmentPrefix = "seg_";
    private float _currentSegmentDuration = 0;
    private string? _currentFileName = null;
    private HlsPlaylist _playlistBuilder = null!;
    public void SetPlaylist(HlsPlaylist playlistBuilder) => _playlistBuilder = playlistBuilder;

    public async Task DownloadSegmentsAsync(
        PlaylistResult playlistResult,
        string streamFolderPath,
        int maxSegmentDuration,
        CancellationToken cancellationToken)
    {
        FileStream? fileStream = null;
        try
        {
            await TryDownloadInitSegmentAsync(playlistResult.InitSegmentUrl, streamFolderPath, _playlistBuilder, cancellationToken);

            foreach (var segment in playlistResult.Segments)
            {
                if (fileStream is null)
                {
                    var index = GetNextSegmentNumber(_playlistBuilder.LastSegmentFileName);
                    _currentFileName = $"{SegmentPrefix}{index}{GetUrlExtension(segment.Url)}";
                    var segmentPath = Path.Combine(streamFolderPath, _currentFileName);
                    fileStream = OpenSegmentFile(segmentPath, FileMode.Append);
                }

                await using var segmentStream = await twitchGqlClient.DownloadAsStreamAsync(segment.Url, cancellationToken);
                await segmentStream.CopyToAsync(fileStream, cancellationToken);

                _currentSegmentDuration += segment.DurationSeconds;
                if (_currentSegmentDuration < maxSegmentDuration)
                    continue;

                _playlistBuilder.AddSegment(_currentFileName!, _currentSegmentDuration);
                await _playlistBuilder.FlushAsync(cancellationToken);
                _currentSegmentDuration = 0f;

                fileStream?.Dispose();
                fileStream = null;
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

    private async Task TryDownloadInitSegmentAsync(
        string? url,
        string streamFolderPath,
        HlsPlaylist playlistBuilder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url) || playlistBuilder.HasInitSegment)
            return;

        var initFileName = $"init{GetUrlExtension(url)}";
        var initPath = Path.Combine(streamFolderPath, initFileName);
        await using var initStream = await twitchGqlClient.DownloadAsStreamAsync(url, cancellationToken);
        await using var fileStream = OpenSegmentFile(initPath, FileMode.Create);
        await initStream.CopyToAsync(fileStream, cancellationToken);
        playlistBuilder.AddInitSegment(initFileName);
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