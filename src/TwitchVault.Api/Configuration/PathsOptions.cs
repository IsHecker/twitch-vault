namespace TwitchVault.Api.Configuration;

public sealed class PathsOptions
{
    public const string SectionName = "Paths";

    public string Database { get; set; } = string.Empty;
    public string Streams { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Logs { get; set; } = string.Empty;
}