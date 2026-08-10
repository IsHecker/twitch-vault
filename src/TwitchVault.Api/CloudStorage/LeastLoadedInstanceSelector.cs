using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.CloudStorage;

public interface IInstanceSelector
{
    Task<ICloudStorageProvider> SelectInstanceForStreamAsync(Domain.Stream stream);
}

public sealed class LeastLoadedInstanceSelector(
    StorageProviderRegistry factory,
    IStreamRepository streamRepository) : IInstanceSelector
{
    public async Task<ICloudStorageProvider> SelectInstanceForStreamAsync(Domain.Stream stream)
    {
        if (!string.IsNullOrEmpty(stream.StorageInstanceId))
            return factory.GetInstance(stream.StorageInstanceId);

        var enabledInstances = factory.GetEnabledInstances().ToList();
        if (enabledInstances.Count == 0)
            throw new InvalidOperationException("No enabled storage instances available.");

        var allStreams = await streamRepository.GetAllAsync();

        var counts = allStreams
            .Where(s => !string.IsNullOrEmpty(s.StorageInstanceId))
            .GroupBy(s => s.StorageInstanceId!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var selected = enabledInstances
            .OrderBy(inst => counts.GetValueOrDefault(inst.ProviderInstanceId, 0))
            .First();

        stream.StorageInstanceId = selected.ProviderInstanceId;
        await streamRepository.UpdateAsync(stream);

        return selected;
    }
}