namespace TwitchVault.Api.CloudStorage;

/// <summary>
/// Wraps a raw <see cref="ICloudStorageProvider"/> with the cross-cutting concerns routing needs
/// (health, capacity, concurrency) without the provider itself knowing any of this exists. 
/// </summary>
public record ManagedStorageInstance(
    ICloudStorageProvider Provider,
    StorageCapacityTracker Capacity,
    SemaphoreConcurrencyGate ConcurrencySlot)
{
    public void ApplyBehaviorUpdate(StorageBehaviorOptions behavior)
    {
        // ConcurrencySlot.UpdateMaxConcurrency(behavior.MaxConcurrentUploads);
        // Capacity.UpdateCapacity(behavior.CapacityBytes);
    }
}