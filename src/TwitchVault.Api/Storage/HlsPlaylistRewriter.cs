using System.Text;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Storage;

public static class HlsPlaylistRewriter
{
    public static async Task RewriteSegmentsAsync(
        string playlistPath,
        string outputPath,
        string urlFilePath,
        CancellationToken cancellationToken)
    {
        using var outputFile = File.OpenWrite(outputPath);
        var remoteUrls = File.ReadLines(urlFilePath).ToList();
        var lines = File.ReadLines(playlistPath);
        var urlIndex = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains(HlsTags.MapPrefix))
            {
                await WriteLineAsync(outputFile, HlsTags.Map(remoteUrls[urlIndex++]), cancellationToken);
                continue;
            }

            if (trimmed.Contains(HlsSegmentNaming.SegmentPrefix))
            {
                await WriteLineAsync(outputFile, remoteUrls[urlIndex++], cancellationToken);
                continue;
            }

            await WriteLineAsync(outputFile, line, cancellationToken);
        }
        await outputFile.FlushAsync(cancellationToken);
    }

    private static ValueTask WriteLineAsync(FileStream output, string text, CancellationToken ct)
        => output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
}