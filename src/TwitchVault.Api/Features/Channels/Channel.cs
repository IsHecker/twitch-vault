namespace TwitchVault.Api.Features.Channels;

public class Channel : Entity<string>
{
    public string Name { get; private set; } = string.Empty;
    public int QualityRank { get; private set; }
    public bool IsLive { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime? LastStreamedAt { get; private set; }

    public ICollection<User> Users { get; private set; } = [];

    private Channel() { }

    public static Channel Create(string id, string name, int qualityRank, bool isArchived)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(qualityRank);

        return new Channel
        {
            Id = id.Trim(),
            Name = name.Trim(),
            QualityRank = qualityRank,
            IsArchived = isArchived,
            IsLive = false,
            LastStreamedAt = null
        };
    }

    public void SetLive(bool isLive) => IsLive = isLive;

    public void ChangeQualityRank(int qualityRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(qualityRank);
        QualityRank = qualityRank;
    }

    public void SetArchivingStatus(bool isArchived) => IsArchived = isArchived;

    public void UpdateLastStreamedAt(DateTime lastStreamedAt) => LastStreamedAt = lastStreamedAt;
}