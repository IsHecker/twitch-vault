namespace TwitchVault.Api.Events;

public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var handlers))
            _handlers[type] = handlers = [];
        handlers.Add(handler);
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