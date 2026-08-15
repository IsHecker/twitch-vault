using System.Text;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage;

public static class HlsPlaylistRewriter
{
    public static async Task RewriteSegmentsAsync(
        string playlistPath,
        string outputPath,
        Dictionary<string, string> urlMap,
        CancellationToken cancellationToken)
    {
        if (urlMap.Count == 0)
            return;

        using var outputFile = File.OpenWrite(outputPath);
        var lines = File.ReadLines(playlistPath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains(HlsTags.MapPrefix))
            {
                var initFileName = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
                if (!urlMap.TryGetValue(initFileName, out var remoteInitUrl))
                    continue;

                await WriteLineAsync(outputFile, HlsTags.Map(remoteInitUrl), cancellationToken);
                continue;
            }

            if (urlMap.TryGetValue(trimmed, out var remoteSegmentUrl))
            {
                await WriteLineAsync(outputFile, remoteSegmentUrl, cancellationToken);
                continue;
            }

            await WriteLineAsync(outputFile, line, cancellationToken);
        }
        await outputFile.FlushAsync(cancellationToken);
    }

    private static ValueTask WriteLineAsync(FileStream output, string text, CancellationToken ct = default)
        => output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
}