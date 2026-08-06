using Newtonsoft.Json;

namespace TwitchVault.Api.Domain;

public sealed class StreamFolder
{
    private const string PlaylistFile = "playlist.m3u8";
    private const string ThumbnailFile = "thumbnail.jpg";
    private const string TimestampFormat = "yyyy-MM-dd HH-mm-ss";

    public string RelativePath { get; init; } = null!;

    [JsonConstructor]
    private StreamFolder() { }

    public static StreamFolder Create(string streamsRoot, string channelName)
    {
        var sanitizedChannel = SanitizeForFileSystem(channelName);
        var timestamp = DateTime.Now.ToString(TimestampFormat);
        var relative = Path.Combine(streamsRoot, sanitizedChannel, timestamp);
        return new StreamFolder { RelativePath = relative ?? string.Empty };
    }

    public string GetAbsolutePath(string contentRootPath) =>
        Path.Combine(contentRootPath, RelativePath);

    public string GetAbsolutePlaylistPath(string contentRootPath) =>
        Path.Combine(GetAbsolutePath(contentRootPath), PlaylistFile);

    public string ThumbnailPath => Path.Combine(RelativePath, ThumbnailFile).Replace('\\', '/');

    public string GetAbsoluteSegmentPath(string contentRootPath, string segmentFileName) =>
        Path.Combine(GetAbsolutePath(contentRootPath), Path.GetFileName(segmentFileName));

    public string GetThumbnailUrl(string baseUrl) =>
        new Uri($"{baseUrl.TrimEnd('/')}/{ThumbnailPath}").AbsoluteUri;

    public void EnsureDirectoryExists(string contentRootPath) =>
        Directory.CreateDirectory(GetAbsolutePath(contentRootPath));

    private static string SanitizeForFileSystem(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = value.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
        return new string(sanitizedChars);
    }
}