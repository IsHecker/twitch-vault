namespace TwitchVault.Api.Models;

public sealed class StreamSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StreamId { get; set; } = null!;
    public int SegmentNumber { get; set; }
    public string Title { get; set; } = null!;
    public string CategoryName { get; set; } = null!;
    public string ThumbnailUrl { get; set; } = null!;
    public DateTime StartedAt { get; set; } = DateTime.Now;
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