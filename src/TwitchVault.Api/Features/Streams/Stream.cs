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
    Remote
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

public sealed class Stream : Entity<string>
{
    public string ChannelId { get; init; } = null!;

    public StreamFolder Folder { get; init; } = null!;
    public StreamStatus Status { get; private set; }
    public StorageLocation StorageLocation { get; private set; }
    public StorageOperationStatus StorageOperationStatus { get; private set; }
    public long SizeBytes { get; private set; }

    public string? StorageInstanceName { get; private set; }

    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; private set; }

    public List<Chapter> Chapters { get; init; } = [];

    public Chapter CurrentChapter => Chapters.LastOrDefault()!;

    private Stream() { }

    public static Stream Create(
        string twitchStreamId,
        string channelId,
        StreamFolder folder,
        DateTime startedAt,
        string initialTitle,
        string initialCategoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twitchStreamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(folder);

        var stream = new Stream
        {
            Id = twitchStreamId.Trim(),
            ChannelId = channelId.Trim(),
            Folder = folder,
            Status = StreamStatus.Recording,
            StorageLocation = StorageLocation.Local,
            StorageOperationStatus = StorageOperationStatus.None,
            SizeBytes = 0,
            StartedAt = startedAt,
            FinishedAt = null,
            Chapters = []
        };

        stream.AddChapter(initialTitle, initialCategoryId, startedAt);
        return stream;
    }

    public void MarkAsFinished(DateTime finishedAt)
    {
        Status = StreamStatus.Finished;
        FinishedAt = finishedAt;

        CurrentChapter?.Complete(finishedAt);
    }

    public void MarkAsStopped(DateTime finishedAt)
    {
        Status = StreamStatus.Stopped;
        FinishedAt = finishedAt;

        CurrentChapter?.Complete(finishedAt);
    }

    public void MarkAsRecording()
    {
        Status = StreamStatus.Recording;
        FinishedAt = null;
    }

    public void MarkAsInterrupted()
    {
        Status = StreamStatus.Interrupted;
    }

    public void AddChapter(string title, string categoryId, DateTime startedAt)
    {
        CurrentChapter?.Complete(startedAt);
        Chapters.Add(Chapter.Create(title, categoryId, startedAt));
    }

    public void SetSize(long sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        SizeBytes = sizeBytes;
    }

    public void SetStorageLocation(StorageLocation storageLocation) => StorageLocation = storageLocation;
    public void SetStorageInstance(string instanceName) => StorageInstanceName = instanceName;
    public void SetStorageOperationStatus(StorageOperationStatus status) => StorageOperationStatus = status;

    public void RequestDeletion() => StorageOperationStatus = StorageOperationStatus.DeleteRequest;

    public bool IsDeleted => StorageOperationStatus
        is StorageOperationStatus.DeleteRequest
        or StorageOperationStatus.Deleting
        or StorageOperationStatus.DeleteFailed;
}