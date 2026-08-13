using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageRouter(
    StorageProviderRegistry providerRegistry,
    StorageQuotaTracker quotaTracker,
    ILogger<StorageRouter> logger)
{
    private int _roundRobinIndex = -1;

    public Result<ICloudStorageProvider> SelectUploader(long streamSizeBytes)
    {
        var candidates = new List<ICloudStorageProvider>();

        foreach (var provider in providerRegistry.EnabledInstances)
        {
            if (!quotaTracker.HasCapacity(provider.Options.Name, streamSizeBytes))
            {
                logger.LogWarning(
                    "Stream size ({Size} bytes) exceeds capacity for provider '{Name}'.",
                    streamSizeBytes,
                    provider.Options.Name);
                continue;
            }

            candidates.Add(provider);
        }

        if (candidates.Count == 0)
            return Error.Failure("No healthy storage provider is available with sufficient capacity.");

        var rawIndex = Interlocked.Increment(ref _roundRobinIndex);
        var index = (rawIndex & 0x7FFFFFFF) % candidates.Count;
        var selected = candidates[index];

        logger.LogInformation(
            "StorageRouter selected provider '{Name}' for payload of {Size} bytes.",
            selected.Options.Name,
            streamSizeBytes);

        return new Result<ICloudStorageProvider>(selected);
    }
}