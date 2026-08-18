using Newtonsoft.Json;

namespace TwitchVault.Api.Domain;

public enum StreamStatus
{
    Recording,
    Finished,
    Interrupted,
    Stopped
}

public enum StorageLocation
{
    Local,
    Remote,
    Both
}

public enum StorageOperationStatus
{
    None,
    Uploading,
    Uploaded,
    UploadFailed,
    DeleteRequest,
    Deleting,
    DeleteFailed
}

public sealed class Stream
{
    public string TwitchStreamId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;

    public StreamFolder Folder { get; set; } = null!;
    public StreamStatus Status { get; private set; }
    public StorageLocation StorageLocation { get; private set; }
    public StorageOperationStatus StorageOperationStatus { get; private set; }
    public long SizeBytes { get; private set; }

    public string? StorageInstanceName { get; private set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; private set; }

    public List<Chapter> Chapters { get; init; } = [];

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
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

    public void AddChapter(string title, string categoryId, DateTime startedAt)
    {
        if (CurrentChapter is not null)
            CurrentChapter.FinishedAt = startedAt;

        Chapters.Add(new Chapter { Title = title, CategoryId = categoryId, StartedAt = startedAt });
    }

    public void SetStorageLocation(StorageLocation storageLocation) => StorageLocation = storageLocation;
    public void SetStorageInstance(string instanceName) => StorageInstanceName = instanceName;
    public void SetStorageOperationStatus(StorageOperationStatus status) => StorageOperationStatus = status;
}