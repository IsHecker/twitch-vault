using System.Text;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage;

// public sealed class HlsPlaylistRewriter
// {
//     public async Task<Result> RewritePlaylistSegmentsOnDiskAsync(
//         string playlistFilePath,
//         IReadOnlyList<string> remoteUrls,
//         CancellationToken cancellationToken = default)
//     {
//         if (!File.Exists(playlistFilePath))
//             return Error.NotFound($"Playlist file not found: {playlistFilePath}");

//         var lines = await File.ReadAllLinesAsync(playlistFilePath, cancellationToken);
//         var outputLines = new List<string>(lines.Length);

//         for (int i = 0; i < lines.Length; i++)
//         {
//             string? line = lines[i];
//             var trimmed = line.Trim();

//             if (trimmed.StartsWith(HlsTags.MapPrefix, StringComparison.OrdinalIgnoreCase))
//             {
//                 var initFileName = ExtractMapFileName(trimmed);
//                 outputLines.Add(HlsTags.Map(remoteUrls[i]));
//                 continue;
//             }

//             if (!trimmed.StartsWith('#') && !string.IsNullOrWhiteSpace(trimmed))
//             {
//                 var fileName = Path.GetFileName(trimmed);
//                 outputLines.Add(remoteUrls[i]);
//                 continue;
//             }

//             outputLines.Add(line);
//         }

//         var tempPath = $"{playlistFilePath}.tmp";
//         await File.WriteAllLinesAsync(tempPath, outputLines, cancellationToken);
//         File.Move(tempPath, playlistFilePath, overwrite: true);

//         return Result.Success;
//     }
//     private static string ExtractMapFileName(string line)
//     {
//         var parts = line.Split("URI=\"", StringSplitOptions.None);
//         if (parts.Length > 1)
//         {
//             var uri = parts[1].TrimEnd('"');
//             return Path.GetFileName(uri);
//         }
//         return string.Empty;
//     }
// }

public static class HlsPlaylistRewriter
{
    public static async Task RewriteSegmentsAsync(
        string playlistFilePath,
        string outputPath,
        List<string> remoteUrls,
        int skipSegmentsCount,
        CancellationToken cancellationToken)
    {
        using var outputFile = File.OpenWrite(outputPath);
        var lines = File.ReadLines(playlistFilePath).ToList();
        var segmentsSkipped = -1;
        var segmentIndex = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Contains(HlsTags.StartTimePrefix))
            {
                await WriteLineAsync(outputFile, line, cancellationToken);
                await WriteLineAsync(
                    outputFile,
                    HlsTags.UploadedTime(EgyptTimeProvider.ToEgyptDateTime(DateTime.UtcNow)),
                    cancellationToken);

                continue;
            }

            if (line.Contains(HlsTags.MapPrefix) && ++segmentsSkipped > skipSegmentsCount)
            {
                await WriteLineAsync(outputFile, HlsTags.Map(remoteUrls[segmentIndex++]), cancellationToken);
                continue;
            }

            if (line.Contains(HlsSegmentNaming.SegmentPrefix) && ++segmentsSkipped > skipSegmentsCount)
            {
                await WriteLineAsync(outputFile, remoteUrls[segmentIndex++], cancellationToken);
                continue;
            }

            await WriteLineAsync(outputFile, line, cancellationToken);
        }
        await outputFile.FlushAsync(cancellationToken);
    }

    private static ValueTask WriteLineAsync(FileStream output, string text, CancellationToken ct = default)
        => output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
}

// public class HlsPlaylistRewriter(string playlistFilePath, string outputPath)
// {
//     private readonly IEnumerator<string> _lines = File.ReadLines(playlistFilePath).GetEnumerator();
//     private readonly FileStream _output = new(
//         outputPath,
//         FileMode.OpenOrCreate,
//         FileAccess.Write,
//         FileShare.ReadWrite,
//         bufferSize: 4096,
//         useAsync: true);

//     public async Task RewriteNextSegmentAsync(
//         string newSegmentName,
//         CancellationToken cancellationToken)
//     {
//         while (_lines.MoveNext())
//         {
//             var line = _lines.Current;

//             if (line.Contains(HlsTags.MapPrefix))
//             {
//                 await WriteLineAsync(HlsTags.Map(newSegmentName), cancellationToken);
//                 await _output.FlushAsync(cancellationToken);
//                 return;
//             }

//             if (line.Contains(HlsSegmentNaming.SegmentPrefix))
//             {
//                 await WriteLineAsync(newSegmentName, cancellationToken);
//                 await _output.FlushAsync(cancellationToken);
//                 return;
//             }

//             await WriteLineAsync(line, cancellationToken);
//         }
//     }

//     public async Task CompleteAsync(CancellationToken cancellationToken)
//     {
//         while (_lines.MoveNext())
//             await WriteLineAsync(_lines.Current, cancellationToken);

//         await _output.FlushAsync(cancellationToken);

//         _lines.Dispose();
//         await _output.DisposeAsync();
//     }

//     private ValueTask WriteLineAsync(string text, CancellationToken ct = default)
//         => _output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);

//     // private async Task RewritePlaylistSegments(
//     //     DiscordUploadResult uploadResult,
//     //     HlsPlaylistRewriter playlistRewriter,
//     //     CancellationToken cancellationToken)
//     // {
//     //     foreach (var attachment in uploadResult.Attachments)
//     //     {
//     //         var segmentUrl = FormatSegmentUrl(uploadResult, attachment);
//     //         await playlistRewriter.RewriteNextSegmentAsync(segmentUrl, cancellationToken);
//     //     }
//     // }
// }