using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage;

public sealed class UniversalPlaylistRewriter
{
    public async Task<Result> RewritePlaylistSegmentsOnDiskAsync(
        string playlistFilePath,
        IReadOnlyList<UploadedSegment> uploadedSegments,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(playlistFilePath))
            return Error.NotFound($"Playlist file not found: {playlistFilePath}");

        var segmentMap = uploadedSegments.ToDictionary(
            s => Path.GetFileName(s.LocalFileName),
            s => s.RemoteUrl,
            StringComparer.OrdinalIgnoreCase);

        var lines = await File.ReadAllLinesAsync(playlistFilePath, cancellationToken);
        var outputLines = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('#') && !string.IsNullOrWhiteSpace(trimmed))
            {
                var fileName = Path.GetFileName(trimmed);
                if (segmentMap.TryGetValue(fileName, out var remoteUrl))
                {
                    outputLines.Add(remoteUrl);
                    continue;
                }
            }

            if (trimmed.StartsWith(HlsTags.MapPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var initFileName = ExtractMapFileName(trimmed);
                if (!string.IsNullOrEmpty(initFileName) && segmentMap.TryGetValue(initFileName, out var remoteInitUrl))
                {
                    outputLines.Add(HlsTags.Map(remoteInitUrl));
                    continue;
                }
            }

            outputLines.Add(line);
        }

        var tempPath = $"{playlistFilePath}.tmp";
        await File.WriteAllLinesAsync(tempPath, outputLines, cancellationToken);
        File.Move(tempPath, playlistFilePath, overwrite: true);

        return Result.Success;
    }
    private static string ExtractMapFileName(string line)
    {
        var parts = line.Split("URI=\"", StringSplitOptions.None);
        if (parts.Length > 1)
        {
            var uri = parts[1].TrimEnd('"');
            return Path.GetFileName(uri);
        }
        return string.Empty;
    }
}