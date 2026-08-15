namespace TwitchVault.Api.CloudStorage;

public readonly record struct RemoteUrl(string LocalFilePath, string Url);

public readonly record struct StorageUploadResponse(string InstanceName, IEnumerable<RemoteUrl> RemoteUrls);