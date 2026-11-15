using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Streams;

[JsonConverter(typeof(StreamFolderJsonConverter))]
public readonly record struct StreamFolder
{
    public const string PlaylistFile = "playlist.m3u8";
    public const string RemoteUrlsFile = "remoteUrls.txt";
    public const string ThumbnailFile = "thumbnail.jpg";
    public const string StoryboardManifestFile = StoryboardConstants.ManifestFileName;
    public const string StripFile = StoryboardConstants.StripFileName;

    public string RelativePath { get; }

    [JsonConstructor]
    public StreamFolder(string relativePath)
    {
        RelativePath = (relativePath ?? string.Empty).Replace('\\', '/');
    }

    public static StreamFolder Create(string streamsRoot, string channelName, string streamId)
    {
        var sanitizedChannel = SanitizeForFileSystem(channelName);
        var relative = Path.Combine(streamsRoot, sanitizedChannel, streamId);
        return new StreamFolder(relative);
    }

    public string AbsolutePath => GetAbsolutePath();

    public string GetAbsolutePath(string? rootPath = null) =>
        string.IsNullOrEmpty(rootPath)
            ? Path.GetFullPath(RelativePath)
            : Path.GetFullPath(Path.Combine(rootPath, RelativePath));

    public string PlaylistPath => Path.Combine(AbsolutePath, PlaylistFile);

    public string RemoteUrlsPath => Path.Combine(AbsolutePath, RemoteUrlsFile);

    public string ThumbnailPath => Path.Combine(RelativePath, ThumbnailFile).Replace('\\', '/');

    public string GetThumbnailUrl(string baseUrl) =>
        new Uri($"{baseUrl.TrimEnd('/')}/{ThumbnailPath}").AbsoluteUri;

    public string StripPath => Path.Combine(AbsolutePath, StripFile);

    public string GetStripUrl(string baseUrl) =>
        new Uri($"{baseUrl.TrimEnd('/')}/{Path.Combine(RelativePath, StripFile).Replace('\\', '/')}").AbsoluteUri;

    public string StoryboardManifestPath => Path.Combine(AbsolutePath, StoryboardManifestFile);

    public string GetStoryboardManifestUrl(string baseUrl) =>
        new Uri($"{baseUrl.TrimEnd('/')}/{Path.Combine(RelativePath, StoryboardManifestFile).Replace('\\', '/')}").AbsoluteUri;

    public string GetAbsolutePlaylistPath(string? rootPath = null) =>
        Path.Combine(GetAbsolutePath(rootPath), PlaylistFile);

    public void EnsureDirectoryExists(string? rootPath = null) =>
        Directory.CreateDirectory(GetAbsolutePath(rootPath));

    public static implicit operator string(StreamFolder folder) => folder.RelativePath;
    public static implicit operator StreamFolder(string path) => new(path);

    public override string ToString() => RelativePath;

    private static string SanitizeForFileSystem(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = value.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
        return new string(sanitizedChars);
    }
}

public sealed class StreamFolderJsonConverter : JsonConverter<StreamFolder>
{
    public override StreamFolder Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, StreamFolder value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.RelativePath);
}