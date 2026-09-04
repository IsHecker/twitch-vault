using System.Collections.Concurrent;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorderRegistry
{
    bool TryRegister(string channelId);
    void Register(string channelId, IStreamRecorder recorder, Task backgroundTask);
    bool TryGet(string channelId, out IStreamRecorder recorder);
    void Remove(string channelId);
    Task[] GetAllBackgroundTasks();
    bool TryGetBackgroundTask(string channelId, out Task backgroundTask);
    IReadOnlyCollection<string> GetActiveChannelIds();
}

public sealed class StreamRecorderRegistry : IStreamRecorderRegistry
{
    private readonly ConcurrentDictionary<string, (IStreamRecorder Recorder, Task BackgroundTask)> _activeRecorders = new();

    public bool TryRegister(string channelId)
    {
        if (_activeRecorders.TryGetValue(channelId, out _))
            return false;

        _activeRecorders[channelId] = (null, null)!;
        return true;
    }

    public void Register(string channelId, IStreamRecorder recorder, Task backgroundTask) =>
        _activeRecorders[channelId] = (recorder, backgroundTask);

    public bool TryGet(string channelId, out IStreamRecorder recorder)
    {
        if (_activeRecorders.TryGetValue(channelId, out var entry))
        {
            recorder = entry.Recorder;
            return true;
        }

        recorder = null!;
        return false;
    }

    public void Remove(string channelId) => _activeRecorders.TryRemove(channelId, out _);

    public Task[] GetAllBackgroundTasks() =>
        _activeRecorders.Values.Select(r => r.BackgroundTask).ToArray();

    public bool TryGetBackgroundTask(string channelId, out Task backgroundTask)
    {
        if (_activeRecorders.TryGetValue(channelId, out var entry) && entry.BackgroundTask is not null)
        {
            backgroundTask = entry.BackgroundTask;
            return true;
        }

        backgroundTask = null!;
        return false;
    }

    public IReadOnlyCollection<string> GetActiveChannelIds() =>
        _activeRecorders.Keys.ToArray();
}