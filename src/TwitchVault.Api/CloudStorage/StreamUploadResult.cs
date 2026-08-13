namespace TwitchVault.Api.CloudStorage;

public record StreamUploadResult(string StorageInstanceId, int SegmentsSkipped, List<string> RemoteUrls);
