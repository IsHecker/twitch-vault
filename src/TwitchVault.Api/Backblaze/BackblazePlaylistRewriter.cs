using System.Text;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Backblaze;

public sealed class BackblazePlaylistRewriter(
    BackblazeStorageService storage,
    IOptions<BackblazeStorageOptions> options)
{
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ts", ".mp4", ".m4s" };

    public async Task<string> RewriteAsync(
        string localPlaylistPath,
        string remotePrefix,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(localPlaylistPath, cancellationToken);
        var urlLifetime = TimeSpan.FromHours(storage.Options.PreSignedUrlLifetimeHours);

        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith('#') && IsMediaFile(trimmed))
            {
                var segmentName = Path.GetFileName(trimmed);
                var objectKey = $"{remotePrefix.TrimEnd('/')}/{segmentName}";
                var preSignedUrl = storage.GetPreSignedUrl(objectKey, urlLifetime)
                    .Replace(options.Value.Host, options.Value.CDNHost);
                sb.AppendLine(preSignedUrl);
            }
            else if (trimmed.StartsWith(HlsTags.MapPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var mapUri = ExtractMapUri(trimmed);
                var initName = Path.GetFileName(mapUri);
                var objectKey = $"{remotePrefix.TrimEnd('/')}/{initName}";
                var preSignedUrl = storage.GetPreSignedUrl(objectKey, urlLifetime);
                sb.AppendLine($"{HlsTags.MapPrefix}=\"{preSignedUrl}\"");
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private static bool IsMediaFile(string line) =>
        !string.IsNullOrWhiteSpace(line) &&
        MediaExtensions.Contains(Path.GetExtension(line));

    private static string ExtractMapUri(string line)
    {
        var parts = line.Split("URI=\"", StringSplitOptions.None);
        if (parts.Length > 1)
        {
            return parts[1].TrimEnd('"');
        }
        return line;
    }
}