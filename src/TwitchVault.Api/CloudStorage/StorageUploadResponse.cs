namespace TwitchVault.Api.CloudStorage;

public readonly record struct RemoteUrl(string FileName, string Url);

public readonly record struct StorageUploadResponse(string InstanceName, IEnumerable<RemoteUrl> RemoteUrls);