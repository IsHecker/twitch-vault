using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Recording.Storyboard;

public record struct StoryboardManifest(
    [property: JsonPropertyName("storyboard")] StoryboardInfo Storyboard,
    [property: JsonPropertyName("strip")] StripInfo Strip);

public record struct StoryboardInfo(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files);

public record struct StripInfo(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows);