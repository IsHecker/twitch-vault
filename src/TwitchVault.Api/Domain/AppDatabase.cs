namespace TwitchVault.Api.Domain;

public class AppDatabase
{
    public List<Channel> Channels { get; set; } = [];
    public List<Stream> Streams { get; set; } = [];
    public List<User> Users { get; set; } = [];
    public List<UserChannel> UserChannels { get; set; } = [];
}




public sealed class LegacyDatabase
{
    public List<LegacyChannel> Channels { get; set; } = [];
    public List<LegacyStream> Streams { get; set; } = [];
    public List<LegacyUser> Users { get; set; } = [];
    public List<LegacyUserChannel> UserChannels { get; set; } = [];
}

public sealed class LegacyChannel
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int QualityRank { get; set; }
    public bool IsLive { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? LastStreamedAt { get; set; }
}

public sealed class LegacyStream
{
    public string Id { get; set; } = null!;
    public string ChannelId { get; set; } = null!;

    public StreamFolder Folder { get; set; } = null!;
    public StreamStatus Status { get; set; }
    public StorageLocation StorageLocation { get; set; }
    public StorageOperationStatus StorageOperationStatus { get; set; }
    public long SizeBytes { get; set; }

    public string? StorageInstanceName { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public List<Chapter> Chapters { get; set; } = [];
}

public sealed class LegacyUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LegacyUserChannel
{
    public Guid UserId { get; set; }
    public string ChannelId { get; set; } = null!;
    public DateTime AddedAt { get; set; }
}