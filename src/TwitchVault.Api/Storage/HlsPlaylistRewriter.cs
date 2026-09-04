using System.Text;
using TwitchVault.Api.Common;
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
        var urlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(urlFilePath))
        {
            foreach (var line in File.ReadLines(urlFilePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var parts = trimmed.Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var fileName = Path.GetFileName(parts[0]);
                    urlMap[fileName] = parts[1];
                }
            }
        }

        using var outputFile = File.OpenWrite(outputPath);
        var lines = File.ReadLines(playlistPath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains(HlsTags.MapPrefix))
            {
                var initFileName = HlsTagReader.ReadTagValue(trimmed, HlsTags.MapPrefix).Trim('"');
                initFileName = Path.GetFileName(initFileName);
                if (urlMap.TryGetValue(initFileName, out var remoteInitUrl))
                {
                    await WriteLineAsync(outputFile, HlsTags.Map(remoteInitUrl), cancellationToken);
                }
                else
                {
                    await WriteLineAsync(outputFile, line, cancellationToken);
                }
                continue;
            }

            if (HlsSegmentNaming.IsSegmentFile(trimmed))
            {
                var segmentFileName = Path.GetFileName(trimmed);
                if (urlMap.TryGetValue(segmentFileName, out var remoteUrl))
                {
                    await WriteLineAsync(outputFile, remoteUrl, cancellationToken);
                }
                else
                {
                    await WriteLineAsync(outputFile, line, cancellationToken);
                }
                continue;
            }

            await WriteLineAsync(outputFile, line, cancellationToken);
        }
        await outputFile.FlushAsync(cancellationToken);
    }

    private static ValueTask WriteLineAsync(FileStream output, string text, CancellationToken ct)
        => output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
}


// public static class HlsPlaylistRewriter
// {
//     public static async Task RewriteSegmentsAsync(
//         string playlistPath,
//         string outputPath,
//         Dictionary<string, string> urlMap,
//         CancellationToken cancellationToken)
//     {
//         if (urlMap.Count == 0)
//             return;

//         using var outputFile = File.OpenWrite(outputPath);
//         var lines = File.ReadLines(playlistPath);

//         foreach (var line in lines)
//         {
//             var trimmed = line.Trim();
//             if (trimmed.Contains(HlsTags.MapPrefix))
//             {
//                 var initFileName = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
//                 if (!urlMap.TryGetValue(initFileName, out var remoteInitUrl))
//                     continue;

//                 await WriteLineAsync(outputFile, HlsTags.Map(remoteInitUrl), cancellationToken);
//                 continue;
//             }

//             if (urlMap.TryGetValue(trimmed, out var remoteSegmentUrl))
//             {
//                 await WriteLineAsync(outputFile, remoteSegmentUrl, cancellationToken);
//                 continue;
//             }

//             await WriteLineAsync(outputFile, line, cancellationToken);
//         }
//         await outputFile.FlushAsync(cancellationToken);
//     }

//     private static ValueTask WriteLineAsync(FileStream output, string text, CancellationToken ct = default)
//         => output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
// }