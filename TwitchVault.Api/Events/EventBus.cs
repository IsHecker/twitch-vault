using System.Collections.Concurrent;

namespace TwitchVault.Api.Events;

public class EventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            _handlers[typeof(TEvent)] = handlers = [];

        handlers.Add(handler);
    }

    public void UnSubscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        handlers.Remove(handler);
    }

    public async Task PublishAsync<TEvent>(TEvent e)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
        foreach (var handler in handlers)
        {
            await ((Func<TEvent, Task>)handler)(e);
        }
    }
}