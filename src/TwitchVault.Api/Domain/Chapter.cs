namespace TwitchVault.Api.Domain;

public sealed class Chapter
{
    public string Title { get; private set; } = string.Empty;
    public string CategoryId { get; private set; } = string.Empty;
    public DateTime StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    private Chapter() { }

    public Chapter(string title, string categoryId, DateTime startedAt, DateTime? finishedAt = null)
    {
        Title = title ?? string.Empty;
        CategoryId = categoryId ?? string.Empty;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
    }

    public static Chapter Create(string title, string categoryId, DateTime startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Chapter(title.Trim(), categoryId ?? string.Empty, startedAt);
    }

    public void Complete(DateTime finishedAt)
    {
        FinishedAt = finishedAt;
    }
}