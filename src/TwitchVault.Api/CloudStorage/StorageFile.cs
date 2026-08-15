namespace TwitchVault.Api.CloudStorage;

public readonly record struct StorageFile(string FileName, string ContentType, Stream Content);