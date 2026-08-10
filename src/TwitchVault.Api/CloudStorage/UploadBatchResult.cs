namespace TwitchVault.Api.CloudStorage;

public record UploadedSegment(string LocalFileName, string RemoteUrl);
public record UploadBatchResult(IReadOnlyList<UploadedSegment> Segments);