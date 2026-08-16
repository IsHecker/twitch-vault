using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.CloudStorage;

// public sealed class StorageProviderRegistry
// {
//     // TODO: Need to make storage options editable in runtime.
//     private readonly record struct ProviderTemplate(Type Type, Type OptionsType);

//     private static readonly Dictionary<CloudProviderType, ProviderTemplate> ProviderTemplates =
//         GetProvidersFromAssembly(typeof(StorageProviderRegistry).Assembly);

//     private readonly Dictionary<string, ManagedStorageInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

//     public IReadOnlyList<ManagedStorageInstance> EnabledInstances { get; }

//     public StorageProviderRegistry(
//         IServiceProvider serviceProvider,
//         IOptions<StorageOptions> options,
//         IConfiguration configuration)
//     {
//         var instanceSections = configuration
//             .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
//             .GetChildren()
//             .ToArray();

//         foreach (var (instanceOptions, configSection) in options.Value.Instances.Zip(instanceSections))
//         {
//             if (!ProviderTemplates.TryGetValue(instanceOptions.Provider, out var template))
//                 continue;

//             var provider = CreateProvider(serviceProvider, template, instanceOptions, configSection);
//             _instances[instanceOptions.Name] = WrapInstance(provider, instanceOptions);
//         }

//         EnabledInstances = options.Value.Instances
//             .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
//             .Select(i => _instances[i.Name])
//             .ToList();
//     }

//     public ManagedStorageInstance GetInstance(string instanceId) =>
//         _instances.TryGetValue(instanceId, out var instance)
//             ? instance
//             : throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");

//     private static ICloudStorageProvider CreateProvider(
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

//     private static ManagedStorageInstance WrapInstance(
//         ICloudStorageProvider provider,
//         StorageInstanceOptions instanceConfig)
//     {
//         var capacity = new StorageCapacityTracker(instanceConfig.Behavior.CapacityBytes);
//         var concurrencyGate = new SemaphoreConcurrencyGate(Math.Max(1, instanceConfig.Behavior.MaxConcurrentUploads));

//         return new ManagedStorageInstance(provider, capacity, concurrencyGate);
//     }

//     private static Dictionary<CloudProviderType, ProviderTemplate> GetProvidersFromAssembly(Assembly assembly)
//     {
//         return assembly.GetTypes()
//             .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
//             .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
//             .Where(x => x.Attribute is not null)
//             .ToDictionary(
//                 x => x.Attribute!.ProviderType,
//                 x => new ProviderTemplate(x.Type, x.Attribute!.OptionsType));
//     }
// }

public sealed class StorageProviderRegistry : IDisposable
{
    private readonly record struct ProviderTemplate(Type Type, Type OptionsType);

    private readonly record struct RegisteredInstance(
        ManagedStorageInstance Instance,
        LiveOptions<StorageInstanceOptions> InstanceOptions,
        ILiveOptions ProviderOptions);

    private static readonly Dictionary<CloudProviderType, ProviderTemplate> ProviderTemplates =
        GetProvidersFromAssembly(typeof(StorageProviderRegistry).Assembly);

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorageProviderRegistry> _logger;
    private readonly IDisposable? _monitorDisposable;
    private readonly ConcurrentDictionary<string, RegisteredInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ManagedStorageInstance> EnabledInstances { get; private set; } = [];

    public StorageProviderRegistry(
        IServiceProvider serviceProvider,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        IConfiguration configuration,
        ILogger<StorageProviderRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;

        Reconcile(optionsMonitor.CurrentValue);
        _monitorDisposable = optionsMonitor.OnChange(Reconcile);
    }

    public ManagedStorageInstance GetInstance(string instanceId) =>
        _instances.TryGetValue(instanceId, out var registered)
            ? registered.Instance
            : throw new InvalidOperationException($"Storage instance '{instanceId}' not found or not configured.");

    private void Reconcile(StorageOptions options)
    {
        var instanceSections = _configuration
            .GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.Instances)}")
            .GetChildren()
            .ToArray();

        foreach (var (instanceOptions, rawSectionConfig) in options.Instances.Zip(instanceSections))
        {
            if (!ProviderTemplates.TryGetValue(instanceOptions.Provider, out var template))
                continue;

            if (_instances.TryGetValue(instanceOptions.Name, out var existing))
            {
                UpdateInstanceOptions(instanceOptions, rawSectionConfig, template, existing);
            }
            else
            {
                _instances[instanceOptions.Name] = CreateRegisteredInstance(template, instanceOptions, rawSectionConfig);
                _logger.LogInformation("Storage instance '{Name}' registered.", instanceOptions.Name);
            }
        }

        HandleRemovedInstances(options.Instances);

        EnabledInstances = options.Instances
            .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
            .Select(i => _instances[i.Name].Instance)
            .ToList();
    }

    private void UpdateInstanceOptions(
        StorageInstanceOptions instanceOptions,
        IConfigurationSection configSection,
        ProviderTemplate template,
        RegisteredInstance existing)
    {
        var providerProperties = configSection.GetSection("Properties").Get(template.OptionsType)!;

        existing.InstanceOptions.Update(instanceOptions);
        existing.ProviderOptions.Update(providerProperties);
        existing.Instance.ApplyBehaviorUpdate(instanceOptions.Behavior);

        _logger.LogInformation("Storage instance '{Name}' reconfigured in place.", instanceOptions.Name);
    }

    private void HandleRemovedInstances(List<StorageInstanceOptions> updatedInstances)
    {
        foreach (var instance in _instances)
        {
            if (!updatedInstances.Exists(i => i.Name == instance.Key))
                _instances.TryRemove(instance.Key, out _);
        }
    }

    private RegisteredInstance CreateRegisteredInstance(
        ProviderTemplate template, StorageInstanceOptions instanceConfig, IConfigurationSection section)
    {
        var providerProperties = section.GetSection("Properties").Get(template.OptionsType)!;

        var instanceOptionsLive = new LiveOptions<StorageInstanceOptions>(instanceConfig);
        var providerOptionsLive = (ILiveOptions)Activator.CreateInstance(
            typeof(LiveOptions<>).MakeGenericType(template.OptionsType), providerProperties)!;

        var provider = (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
            _serviceProvider, template.Type, instanceOptionsLive, providerOptionsLive);

        var capacity = new StorageCapacityTracker(instanceConfig.Behavior.CapacityBytes);
        // var gate = new ResizableConcurrencyGate(instanceConfig.Behavior.MaxConcurrentUploads);
        var concurrencyGate = new SemaphoreConcurrencyGate(Math.Max(1, instanceConfig.Behavior.MaxConcurrentUploads));

        var managed = new ManagedStorageInstance(provider, capacity, concurrencyGate);

        return new RegisteredInstance(managed, instanceOptionsLive, providerOptionsLive);
    }

    private static Dictionary<CloudProviderType, ProviderTemplate> GetProvidersFromAssembly(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(ICloudStorageProvider)))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<StorageProviderAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToDictionary(
                x => x.Attribute!.ProviderType,
                x => new ProviderTemplate(x.Type, x.Attribute!.OptionsType));
    }

    public void Dispose() => _monitorDisposable?.Dispose();
}