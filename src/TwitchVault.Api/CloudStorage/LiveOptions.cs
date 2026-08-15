namespace TwitchVault.Api.CloudStorage;

/// <summary>
/// Non-generic handle so the registry can hold and update a LiveOptions&lt;T&gt; for a provider-specific
/// options type it only knows via reflection (CloudProviderType -> Type), without needing T at compile time.
/// </summary>
public interface ILiveOptions
{
    void Update(object newValue);
}

/// <summary>
/// A mutable reference cell for a config-derived options object. Providers depend on this instead
/// of a raw options instance so a config reload can swap the value in place — no DI re-activation,
/// no losing the provider's own state (health history, capacity counters, in-flight count).
/// </summary>
public sealed class LiveOptions<T>(T initial) : ILiveOptions where T : class
{
    private T _value = initial;

    public T Value => Volatile.Read(ref _value);

    public void Update(T newValue) => Volatile.Write(ref _value, newValue);

    void ILiveOptions.Update(object newValue) => Update((T)newValue);
}