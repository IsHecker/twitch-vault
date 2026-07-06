using System.Runtime.CompilerServices;
using TwitchVault.Api.Common;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording.HLS;

public interface ISegmentDownloader
{
    IAsyncEnumerable<(string FileName, float Duration)> DownloadSegmentsAsync(
        string streamFolderPath,
        ManifestExtractionResult manifestResult,
        IHlsPlaylist hlsPlaylist,
        CancellationToken cancellationToken = default);

    (string FileName, float Duration) CloseSegment();
}

public class SegmentDownloader(
    ITwitchGqlClient twitchGqlClient,
    SegmentStateTracker segmentStateTracker,
    IFileSystem fileSystem) : ISegmentDownloader
{
    public async IAsyncEnumerable<(string FileName, float Duration)> DownloadSegmentsAsync(
        string streamFolderPath,
        ManifestExtractionResult manifestResult,
        IHlsPlaylist hlsPlaylist,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stream? fileStream = null;

        try
        {
            if (!string.IsNullOrEmpty(manifestResult.InitSegmentUrl) && !hlsPlaylist.HasInitSegment)
                yield return await DownloadInitSegmentAsync(manifestResult.InitSegmentUrl, streamFolderPath, cancellationToken);

            foreach (var segment in manifestResult.Segments)
            {
                if (fileStream is null)
                {
                    string fileName = segmentStateTracker.GetOrStartSegment(hlsPlaylist.LastSegmentFileName, GetUrlExtension(segment.Url));
                    fileStream = fileSystem.OpenWrite(Path.Combine(streamFolderPath, fileName), FileMode.Append);
                }

                await using var segmentStream = await twitchGqlClient.DownloadAsStreamAsync(segment.Url, cancellationToken);
                await segmentStream.CopyToAsync(fileStream!, cancellationToken);

                segmentStateTracker.AddDuration(segment.Duration);
                if (!segmentStateTracker.IsFull)
                    continue;

                yield return CloseSegment();

                fileStream?.Dispose();
                fileStream = null;
            }
        }
        finally
        {
            fileStream?.Dispose();
        }
    }

    public (string FileName, float Duration) CloseSegment() => segmentStateTracker.CloseSegment();

    private async Task<(string FileName, float Duration)> DownloadInitSegmentAsync(
        string url,
        string streamFolderPath,
        CancellationToken cancellationToken)
    {
        var initFileName = $"init{GetUrlExtension(url)}";
        var initPath = Path.Combine(streamFolderPath, initFileName);
        await using var initStream = await twitchGqlClient.DownloadAsStreamAsync(url, cancellationToken);
        await using var fileStream = fileSystem.OpenWrite(initPath, FileMode.Create);
        await initStream.CopyToAsync(fileStream, cancellationToken);
        return (initFileName, 0);
    }

    private static string GetUrlExtension(string url) =>
        Path.GetExtension(new Uri(url).AbsolutePath);
}