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
    public int ChannelId { get; set; }
    public string Title { get; init; } = null!;
    public string GameDisplayName { get; init; } = null!;
    public string ThumbnailUrl { get; set; } = null!;
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
    public StreamStatus Status { get; set; } = StreamStatus.Recording;
    public string FolderPath { get; set; } = string.Empty;
    public bool MarkForDeletion { get; set; }

    public void MarkAsFinished()
    {
        Status = StreamStatus.Finished;
        FinishedAt ??= DateTime.Now;
    }

    public void MarkAsStopped()
    {
        Status = StreamStatus.Stopped;
        FinishedAt ??= DateTime.Now;
    }
}