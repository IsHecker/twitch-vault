using System.Text;

namespace TwitchVault.Api.Features.Storage;

public static class HlsPlaylistRewriter
{
    private static readonly Encoding OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task RewriteSegmentsAsync(
        string playlistPath,
        string outputPath,
        string urlFilePath,
        CancellationToken cancellationToken)
    {
        var urlMap = LoadUrlMap(urlFilePath);
        await using var writer = new StreamWriter(File.Create(outputPath), OutputEncoding);

        foreach (var line in File.ReadLines(playlistPath))
        {
            await writer.WriteLineAsync(RewriteLine(line, urlMap));
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> LoadUrlMap(string urlFilePath)
    {
        var urlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(urlFilePath))
            return urlMap;

        foreach (var line in File.ReadLines(urlFilePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var parts = trimmed.Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var fileName = Path.GetFileName(parts[0]);
            urlMap[fileName] = parts[1];
        }

        return urlMap;
    }

    private static string RewriteLine(string line, Dictionary<string, string> urlMap)
    {
        var trimmed = line.Trim();

        if (trimmed.Contains(HlsTags.MapPrefix))
        {
            var initFileName = Path.GetFileName(HlsTagReader.ReadTagValue(trimmed, HlsTags.MapPrefix).Trim('"'));
            return urlMap.TryGetValue(initFileName, out var remoteInitUrl) ? HlsTags.Map(remoteInitUrl) : line;
        }

        if (HlsSegmentNaming.IsSegmentFile(trimmed))
        {
            var segmentFileName = Path.GetFileName(trimmed);
            return urlMap.TryGetValue(segmentFileName, out var remoteUrl) ? remoteUrl : line;
        }

        return line;
    }
}