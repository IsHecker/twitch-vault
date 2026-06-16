namespace TwitchVault.Api.Domain;

public sealed class Chapter
{
    public string Title { get; set; } = null!;
    public string Category { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}