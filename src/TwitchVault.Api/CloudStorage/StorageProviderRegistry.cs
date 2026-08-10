using Microsoft.Extensions.Options;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageProviderRegistry
{
    private readonly Dictionary<string, ICloudStorageProvider> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly StorageOptions _options;

    public StorageProviderRegistry(
        IEnumerable<ICloudStorageProvider> providerTemplates,
        IServiceProvider serviceProvider,
        IOptions<StorageOptions> options)
    {
        _options = options.Value;

        var templateMap = providerTemplates.ToDictionary(p => p.ProviderType);

        foreach (var config in _options.Instances)
        {
            if (!templateMap.TryGetValue(config.Type, out var template))
                continue;

            var instance = (ICloudStorageProvider)ActivatorUtilities.CreateInstance(serviceProvider, template.GetType())!;
            instance.Initialize(config.Id, config.Settings);
            _instances[config.Id] = instance;
        }
    }

    public ICloudStorageProvider GetInstance(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
            return instance;

        throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");
    }

    public IEnumerable<ICloudStorageProvider> GetEnabledInstances()
    {
        if (_options.EnabledInstances.Count == 0)
            return _instances.Values;

        return _options.EnabledInstances
            .Where(_instances.ContainsKey)
            .Select(id => _instances[id]);
    }
}