namespace TwitchVault.Api.Backblaze;

public sealed class BackblazeStorageOptions
{
    public const string SectionName = "BackblazeStorage";

    public required string BucketName { get; init; }
    public required string KeyId { get; init; }
    public required string ApplicationKey { get; init; }
    public required string Host { get; init; }
    public required string CDNHost { get; init; }
    public required string AuthenticationRegion { get; init; }
    public int LocalRetentionDays { get; init; }
    public int PreSignedUrlLifetimeHours { get; init; }
}