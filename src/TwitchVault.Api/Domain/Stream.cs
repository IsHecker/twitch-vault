using System.Text.Json.Serialization;

namespace TwitchVault.Api.Domain;

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
    public StreamStatus Status { get; private set; } = StreamStatus.Recording;
    public bool MarkForDeletion { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; private set; }

    public List<Chapter> Chapters { get; private set; } = [];

    [JsonIgnore]
    public Chapter CurrentChapter => Chapters.LastOrDefault()!;

    public void MarkAsFinished(DateTime finishedAt)
    {
        Status = StreamStatus.Finished;
        FinishedAt = finishedAt;

        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = FinishedAt;
    }

    public void MarkAsStopped(DateTime finishedAt)
    {
        Status = StreamStatus.Stopped;
        FinishedAt = finishedAt;

        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = FinishedAt;
    }

    public void MarkAsRecording()
    {
        Status = StreamStatus.Recording;
        FinishedAt = null;

        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = null;
    }

    public void MarkAsInterrupted()
    {
        Status = StreamStatus.Interrupted;
    }

    public void AddChapter(string title, string category, DateTime startedAt)
    {
        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = startedAt;

        Chapters.Add(new Chapter { Title = title, Category = category, StartedAt = startedAt });
    }
}