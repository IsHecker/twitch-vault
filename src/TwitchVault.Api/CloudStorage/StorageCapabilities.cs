namespace TwitchVault.Api.CloudStorage;

public readonly record struct StorageCapabilities(int MaxBatchSize, long MaxFileSizeBytes);