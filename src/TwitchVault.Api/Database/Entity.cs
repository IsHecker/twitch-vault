namespace TwitchVault.Api.Domain;

public abstract class Entity<TKey>
{
    public TKey Id { get; init; } = default!;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    protected Entity() { }
}