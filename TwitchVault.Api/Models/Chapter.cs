namespace TwitchVault.Api.Models;

public sealed class Chapter
{
    public string Title { get; set; } = null!;
    public string Category { get; set; } = null!;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
}