using Newtonsoft.Json;

namespace TwitchVault.Api.Domain;

public enum StreamStatus
{
    Recording,
    Finished,
    Interrupted,
    Stopped,
    PendingDeletion,
    Deleting
}

public enum StorageLocation
{
    Local,
    Uploading,
    Both,
    Remote
}

public sealed class Stream
{
    public string TwitchStreamId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;

    public StreamFolder Folder { get; set; } = null!;
    public string ThumbnailUrl { get; private set; } = null!;
    public StreamStatus Status { get; private set; } = StreamStatus.Recording;
    public StorageLocation Storage { get; private set; } = StorageLocation.Local;
    
    /// <summary>
    /// Assigned storage instance ID (e.g. "discord-main", "dropbox-acc1", "s3-backup")
    /// </summary>
    public string? StorageInstanceId { get; set; }
    
    public bool MarkForDeletion { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; private set; }

    public List<Chapter> Chapters { get; init; } = [];

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public Chapter CurrentChapter => Chapters.LastOrDefault()!;

    public void SetStatus(StreamStatus status) => Status = status;

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

    public void AddChapter(string title, string categoryId, DateTime startedAt)
    {
        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = startedAt;

        Chapters.Add(new Chapter { Title = title, CategoryId = categoryId, StartedAt = startedAt });
    }

    public void SetThumbnailUrl(string thumbnailUrl) => ThumbnailUrl = thumbnailUrl;
    public void SetStorageLocation(StorageLocation storageLocation) => Storage = storageLocation;
}