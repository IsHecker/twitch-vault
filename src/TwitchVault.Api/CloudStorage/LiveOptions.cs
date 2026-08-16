namespace TwitchVault.Api.CloudStorage;

public interface ILiveOptions
{
    void Update(object newValue);
}

public sealed class LiveOptions<T>(T initial) : ILiveOptions where T : class
{
    private T _value = initial;

    public T Value => Volatile.Read(ref _value);

    public void Update(T newValue) => Volatile.Write(ref _value, newValue);

    void ILiveOptions.Update(object newValue) => Update((T)newValue);
}