namespace TwitchVault.Api.CloudStorage;

public interface IStorageRoutingStrategy
{
    IReadOnlyList<ManagedStorageInstance> Order(IReadOnlyList<ManagedStorageInstance> candidates);
}

public sealed class RoundRobinStrategy : IStorageRoutingStrategy
{
    private int _cursor = -1;

    public IReadOnlyList<ManagedStorageInstance> Order(IReadOnlyList<ManagedStorageInstance> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var rawIndex = Interlocked.Increment(ref _cursor);
        var start = (rawIndex & 0x7FFFFFFF) % candidates.Count;

        return candidates.Skip(start).Concat(candidates.Take(start)).ToList();
    }
}