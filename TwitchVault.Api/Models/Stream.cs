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
    public int TotalSegments { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }

    public StreamSegment StreamSegment { get; set; } = null!;
}