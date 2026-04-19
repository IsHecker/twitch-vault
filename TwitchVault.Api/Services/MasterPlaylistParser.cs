using TwitchVault.Api.Common;

namespace TwitchVault.Api.Services;

public record struct MediaPlaylist(int Bandwidth, string Url);

public static class MasterPlaylistParser
{
    public static MediaPlaylist[] ParseVariants(string masterPlaylist)
    {
        var lines = masterPlaylist.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        var variants = new List<MediaPlaylist>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal))
                continue;

            var bandwidthStr = HlsTagReader.ReadTagValue(line, "BANDWIDTH", ',');
            if (!int.TryParse(bandwidthStr, out var bandwidth))
                continue;

            if (i + 1 >= lines.Length)
                continue;

            var url = lines[++i].Trim();
            variants.Add(new(bandwidth, url));
        }

        return variants.OrderBy(v => v.Bandwidth).ToArray();
    }
}