using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.CloudStorage;

// TODO: Implementation needs some cleaning
public sealed class StorageProviderRegistry : IDisposable
{
    private readonly record struct ProviderTemplate(Type Type, Type? OptionsType);

    private readonly record struct RegisteredInstance(
        ManagedStorageInstance Instance,
        LiveOptions<StorageInstanceOptions> InstanceOptions,
        ILiveOptions? ProviderOptions);

    private static readonly Dictionary<CloudProviderType, ProviderTemplate> ProviderTemplates =
        GetProvidersFromAssembly(typeof(StorageProviderRegistry).Assembly);

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorageProviderRegistry> _logger;
    private readonly IDisposable? _monitorDisposable;
    private readonly ConcurrentDictionary<string, RegisteredInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<ManagedStorageInstance> _enabledInstances = [];
    public IReadOnlyList<ManagedStorageInstance> EnabledInstances => Volatile.Read(ref _enabledInstances);

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
            if (!instanceOptions.Enabled)
                continue;

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

        var newEnabled = options.Instances
            .Where(i => i.Enabled && _instances.ContainsKey(i.Name))
            .Select(i => _instances[i.Name].Instance)
            .ToList();

        Volatile.Write(ref _enabledInstances, newEnabled);
    }

    private void UpdateInstanceOptions(
        StorageInstanceOptions instanceOptions,
        IConfigurationSection configSection,
        ProviderTemplate template,
        RegisteredInstance existing)
    {
        existing.InstanceOptions.Update(instanceOptions);

        if (template.OptionsType is not null)
            existing.ProviderOptions!.Update(ResolveProviderOptions(configSection, template.OptionsType));

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
        ProviderTemplate template,
        StorageInstanceOptions instanceConfig,
        IConfigurationSection configSection)
    {
        var instanceOptionsLive = new LiveOptions<StorageInstanceOptions>(instanceConfig);

        ILiveOptions? providerOptionsLive = null;
        object[] extraArgs = [];

        if (template.OptionsType is not null)
        {
            providerOptionsLive = (ILiveOptions)Activator.CreateInstance(
                typeof(LiveOptions<>).MakeGenericType(template.OptionsType),
                ResolveProviderOptions(configSection, template.OptionsType))!;

            extraArgs = [providerOptionsLive];
        }

        var provider = (ICloudStorageProvider)ActivatorUtilities.CreateInstance(
            _serviceProvider, template.Type, [instanceOptionsLive, .. extraArgs]);

        var capacity = new StorageCapacityGate(instanceConfig.Behavior.StorageCapacityBytes);

        var managed = new ManagedStorageInstance(provider, capacity);

        return new RegisteredInstance(managed, instanceOptionsLive, providerOptionsLive);
    }

    private static object ResolveProviderOptions(IConfigurationSection section, Type optionsType)
        => section.GetSection("Properties").Get(optionsType) ?? Activator.CreateInstance(optionsType)!;

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