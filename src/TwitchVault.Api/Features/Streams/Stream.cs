namespace TwitchVault.Api.Features.Streams;

public sealed class Stream : Entity<string>
{
    public string? ChannelId { get; init; }

    public StreamFolder Folder { get; private set; } = null!;
    public StreamStatus Status { get; private set; }
    public StorageOperationStatus StorageOperationStatus { get; private set; }
    public long SizeBytes { get; private set; }

    public string? StorageInstanceName { get; private set; }

    public string? VodId { get; private set; }
    public bool IsVodChecked { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    public List<Chapter> Chapters { get; init; } = [];

    public Chapter CurrentChapter => Chapters.LastOrDefault()!;

    public Channel Channel { get; init; } = null!;

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
            StorageOperationStatus = StorageOperationStatus.None,
            SizeBytes = 0,
            StartedAt = startedAt,
            FinishedAt = null,
            Chapters = []
        };

        stream.AddChapter(initialTitle, initialCategoryId, startedAt);
        return stream;
    }

    public void MarkAsFinalizing(DateTime finishedAt)
    {
        Status = StreamStatus.Finalizing;
        FinishedAt = finishedAt;

        CurrentChapter?.Complete(finishedAt);
    }

    public void UpdateFolder(StreamFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Folder = folder;
    }

    public void MarkAsFinished()
    {
        Status = StreamStatus.Finished;
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

    public void SetStorageInstance(string instanceName) => StorageInstanceName = instanceName;
    public void SetStorageOperationStatus(StorageOperationStatus status) => StorageOperationStatus = status;

    public void UpdateStartedAt(DateTime startedAt) => StartedAt = startedAt;
    public void SetVodId(string? vodId) => VodId = vodId;
    public void MarkVodChecked() => IsVodChecked = true;

    public void RequestDeletion() => StorageOperationStatus = StorageOperationStatus.DeleteRequest;

    public bool IsDeleted => StorageOperationStatus
        is StorageOperationStatus.DeleteRequest
        or StorageOperationStatus.Deleting
        or StorageOperationStatus.DeleteFailed;
}