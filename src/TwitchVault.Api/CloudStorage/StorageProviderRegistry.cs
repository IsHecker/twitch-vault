using System.Reflection;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageProviderRegistry
{
    // TODO: Need to make storage options editable in runtime.
    private readonly Dictionary<string, ICloudStorageProvider> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StorageInstanceOptions> _optionsMap = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ICloudStorageProvider> EnabledInstances { get; }

    public StorageProviderRegistry(
        IServiceProvider serviceProvider,
        IOptions<StorageOptions> options,
        IConfiguration configuration)
    {
        var providerTemplates = GetProvidersFromAssembly(Assembly.GetExecutingAssembly());
        var instanceSections = configuration
            .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
            .GetChildren()
            .ToArray();

        foreach (var (instanceConfig, section) in options.Value.Instances.Zip(instanceSections))
        {
            if (!providerTemplates.TryGetValue(instanceConfig.Provider, out var template))
                continue;

            _instances[instanceConfig.Name] = CreateInstance(serviceProvider, template, instanceConfig, section);
            _optionsMap[instanceConfig.Name] = instanceConfig;
        }

        EnabledInstances = options.Value.Instances
            .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
            .Select(i => _instances[i.Name])
            .ToList();
    }

    public ICloudStorageProvider GetInstance(string instanceId) =>
        _instances.TryGetValue(instanceId, out var instance)
            ? instance
            : throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");

    public StorageInstanceOptions? GetInstanceOptions(string instanceId) =>
        _optionsMap.TryGetValue(instanceId, out var options) ? options : null;

    private static ICloudStorageProvider CreateInstance(
        IServiceProvider serviceProvider,
        (Type OptionsType, Type ProviderType) template,
        StorageInstanceOptions instanceConfig,
        IConfigurationSection section)
    {
        var providerProperties = section.GetSection("Properties").Get(template.OptionsType)!;

        return (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
            serviceProvider,
            template.ProviderType,
            instanceConfig,
            providerProperties);
    }

    private static Dictionary<CloudProviderType, (Type OptionsType, Type ProviderType)> GetProvidersFromAssembly(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToDictionary(x => x.Attribute!.ProviderType, x => (x.Attribute!.OptionsType, x.Type));
    }
}


// public sealed class StorageProviderFactory(
//     IServiceProvider serviceProvider,
//     IOptionsMonitor<StorageOptions> optionsMonitor,
//     IConfiguration configuration)
// {
//     private readonly Dictionary<CloudProviderType, (Type OptionsType, Type ProviderType)> _providerTemplates =
//         GetProvidersFromAssembly(Assembly.GetExecutingAssembly());

//     public ICloudStorageProvider Create(string instanceName)
//     {
//         var instanceConfig = GetInstanceOptions(instanceName)
//             ?? throw new InvalidOperationException($"Storage instance '{instanceName}' not found or not configured.");

//         return Create(instanceConfig);
//     }

//     public ICloudStorageProvider Create(StorageInstanceOptions instanceConfig)
//     {
//         ArgumentNullException.ThrowIfNull(instanceConfig);

//         if (!_providerTemplates.TryGetValue(instanceConfig.Provider, out var template))
//             throw new InvalidOperationException($"No storage provider registered for provider type '{instanceConfig.Provider}'.");

//         var section = GetInstanceSection(instanceConfig.Name);
//         var providerProperties = section is not null
//             ? section.GetSection("Properties").Get(template.OptionsType)
//             : null;
//         providerProperties ??= Activator.CreateInstance(template.OptionsType)!;

//         return (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
//             serviceProvider,
//             template.ProviderType,
//             instanceConfig,
//             providerProperties);
//     }

//     public IReadOnlyList<ICloudStorageProvider> CreateEnabledInstances()
//     {
//         return optionsMonitor.CurrentValue.Instances
//             .Where(i => i.Enabled)
//             .Select(Create)
//             .ToList();
//     }

//     public StorageInstanceOptions? GetInstanceOptions(string instanceName)
//     {
//         return optionsMonitor.CurrentValue.Instances
//             .FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
//     }

//     private IConfigurationSection? GetInstanceSection(string instanceName)
//     {
//         var currentInstances = optionsMonitor.CurrentValue.Instances;
//         var instanceSections = configuration
//             .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
//             .GetChildren()
//             .ToArray();

//         var index = -1;
//         for (var i = 0; i < currentInstances.Count; i++)
//         {
//             if (string.Equals(currentInstances[i].Name, instanceName, StringComparison.OrdinalIgnoreCase))
//             {
//                 index = i;
//                 break;
//             }
//         }

//         return index >= 0 && index < instanceSections.Length ? instanceSections[index] : null;
//     }

//     private static Dictionary<CloudProviderType, (Type OptionsType, Type ProviderType)> GetProvidersFromAssembly(Assembly assembly)
//     {
//         return assembly.GetTypes()
//             .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
//             .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
//             .Where(x => x.Attribute is not null)
//             .ToDictionary(x => x.Attribute!.ProviderType, x => (x.Attribute!.OptionsType, x.Type));
//     }
// }