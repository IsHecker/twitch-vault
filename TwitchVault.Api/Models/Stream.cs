namespace TwitchVault.Api.Models;

public enum StreamStatus
{
    Recording,
    Finished,
    Interrupted,
    Stopped
}

public sealed class Stream
{
    public string TwitchStreamId { get; set; } = null!;
    public string? TwitchVodId { get; set; }
    public string ChannelId { get; set; } = null!;
    public string FolderPath { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = null!;
    public StreamStatus Status { get; set; } = StreamStatus.Recording;
    public bool MarkForDeletion { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }

    public List<Chapter> Chapters { get; set; } = null!;
    public Chapter CurrentChapter => Chapters.LastOrDefault()!;

    public void MarkAsFinished()
    {
        Status = StreamStatus.Finished;
        FinishedAt = DateTime.Now;
        CurrentChapter.FinishedAt = FinishedAt;
    }

    public void MarkAsStopped()
    {
        Status = StreamStatus.Stopped;
        FinishedAt = DateTime.Now;
        CurrentChapter.FinishedAt = FinishedAt;
    }

    public void AddChapter(string title, string category)
    {
        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = DateTime.Now;

        Chapters.Add(new Chapter { Title = title, Category = category });
    }
}