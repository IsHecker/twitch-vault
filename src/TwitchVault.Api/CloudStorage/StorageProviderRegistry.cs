using System.Reflection;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.CloudStorage;

public sealed class StorageProviderRegistry
{
    // TODO: Need to make storage options editable in runtime.
    private readonly record struct ProviderTemplate(Type Type, Type OptionsType);

    private static readonly Dictionary<CloudProviderType, ProviderTemplate> ProviderTemplates =
        GetProvidersFromAssembly(typeof(StorageProviderRegistry).Assembly);

    private readonly Dictionary<string, ManagedStorageInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ManagedStorageInstance> EnabledInstances { get; }

    public StorageProviderRegistry(
        IServiceProvider serviceProvider,
        IOptions<StorageOptions> options,
        IConfiguration configuration)
    {
        var instanceSections = configuration
            .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
            .GetChildren()
            .ToArray();

        foreach (var (instanceConfig, section) in options.Value.Instances.Zip(instanceSections))
        {
            if (!ProviderTemplates.TryGetValue(instanceConfig.Provider, out var template))
                continue;

            var provider = CreateProvider(serviceProvider, template, instanceConfig, section);
            _instances[instanceConfig.Name] = WrapInstance(provider, instanceConfig);
        }

        EnabledInstances = options.Value.Instances
            .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
            .Select(i => _instances[i.Name])
            .ToList();
    }

    public ManagedStorageInstance GetInstance(string instanceId) =>
        _instances.TryGetValue(instanceId, out var instance)
            ? instance
            : throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");

    private static ICloudStorageProvider CreateProvider(
        IServiceProvider serviceProvider,
        ProviderTemplate template,
        StorageInstanceOptions instanceConfig,
        IConfigurationSection section)
    {
        var providerProperties = section.GetSection("Properties").Get(template.OptionsType)!;

        return (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
            serviceProvider,
            template.Type,
            instanceConfig,
            providerProperties);
    }

    private static ManagedStorageInstance WrapInstance(
        ICloudStorageProvider provider,
        StorageInstanceOptions instanceConfig)
    {
        var capacity = new StorageCapacityTracker(instanceConfig.Behavior.CapacityBytes);
        var concurrencyGate = new SemaphoreConcurrencyGate(Math.Max(1, instanceConfig.Behavior.MaxConcurrentUploads));

        return new ManagedStorageInstance(provider, capacity, concurrencyGate);
    }

    private static Dictionary<CloudProviderType, ProviderTemplate> GetProvidersFromAssembly(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToDictionary(
                x => x.Attribute!.ProviderType,
                // FIX: the original constructed `new ProviderTemplate(x.Attribute!.OptionsType, x.Type)` —
                // backwards relative to the (Type, OptionsType) declaration order above. That meant
                // ActivatorUtilities.CreateInstance below was handed the *options* type as the thing to
                // instantiate, and Get(template.OptionsType) was handed the *provider* type to bind
                // config into — every registered provider would have failed to construct at startup.
                x => new ProviderTemplate(x.Type, x.Attribute!.OptionsType));
    }
}


// public sealed class StorageProviderRegistry
// {
//     // TODO: Need to make storage options editable in runtime.
//     private readonly record struct ProviderTemplate(Type Type, Type OptionsType);
//     private static readonly Dictionary<CloudProviderType, ProviderTemplate> ProviderTemplates =
//         GetProvidersFromAssembly(typeof(StorageProviderRegistry).Assembly);

//     private readonly Dictionary<string, ICloudStorageProvider> _instances = new(StringComparer.OrdinalIgnoreCase);

//     public IReadOnlyList<ICloudStorageProvider> EnabledInstances { get; }

//     public StorageProviderRegistry(
//         IServiceProvider serviceProvider,
//         IOptions<StorageOptions> options,
//         IConfiguration configuration)
//     {
//         var instanceSections = configuration
//             .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
//             .GetChildren()
//             .ToArray();

//         foreach (var (instanceConfig, section) in options.Value.Instances.Zip(instanceSections))
//         {
//             if (!ProviderTemplates.TryGetValue(instanceConfig.Provider, out var template))
//                 continue;

//             _instances[instanceConfig.Name] = CreateInstance(serviceProvider, template, instanceConfig, section);
//         }

//         EnabledInstances = options.Value.Instances
//             .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
//             .Select(i => _instances[i.Name])
//             .ToList();
//     }

//     public ICloudStorageProvider GetInstance(string instanceId) =>
//         _instances.TryGetValue(instanceId, out var instance)
//             ? instance
//             : throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");

//     private static ICloudStorageProvider CreateInstance(
//         IServiceProvider serviceProvider,
//         ProviderTemplate template,
//         StorageInstanceOptions instanceConfig,
//         IConfigurationSection section)
//     {
//         var providerProperties = section.GetSection("Properties").Get(template.OptionsType)!;

//         return (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
//             serviceProvider,
//             template.Type,
//             instanceConfig,
//             providerProperties);
//     }

//     private static Dictionary<CloudProviderType, ProviderTemplate> GetProvidersFromAssembly(Assembly assembly)
//     {
//         return assembly.GetTypes()
//             .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
//             .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
//             .Where(x => x.Attribute is not null)
//             .ToDictionary(x => x.Attribute!.ProviderType, x => new ProviderTemplate(x.Attribute!.OptionsType, x.Type));
//     }
// }