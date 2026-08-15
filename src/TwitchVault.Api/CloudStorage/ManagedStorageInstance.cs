namespace TwitchVault.Api.CloudStorage;

/// <summary>
/// Wraps a raw <see cref="ICloudStorageProvider"/> with the cross-cutting concerns routing needs
/// (health, capacity, concurrency) without the provider itself knowing any of this exists. 
/// </summary>
public readonly record struct ManagedStorageInstance(
    ICloudStorageProvider Provider,
    StorageCapacityTracker Capacity,
    SemaphoreConcurrencyGate ConcurrencySlot);