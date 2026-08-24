namespace TwitchVault.Api.Domain;

public class Channel
{
    public string Id { get; init; } = null!;
    public string Name { get; private set; } = string.Empty;
    public int QualityRank { get; private set; }
    public bool IsLive { get; private set; }
    public bool ShouldRecord { get; private set; }
    public DateTime? LastStreamedAt { get; private set; }

    public Channel() { }

    public static Channel Create(string id, string name, int qualityRank = 0, bool shouldRecord = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(qualityRank);

        return new Channel
        {
            Id = id.Trim(),
            Name = name.Trim(),
            QualityRank = qualityRank,
            ShouldRecord = shouldRecord,
            IsLive = false,
            LastStreamedAt = null
        };
    }

    public void SetLive(bool isLive) => IsLive = isLive;

    public void UpdateQualityRank(int qualityRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(qualityRank);
        QualityRank = qualityRank;
    }

    public void SetRecordingStatus(bool shouldRecord) => ShouldRecord = shouldRecord;

    public void UpdateLastStreamedAt(DateTime lastStreamedAt) => LastStreamedAt = lastStreamedAt;

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
    }
}