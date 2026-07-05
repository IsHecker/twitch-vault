using System.Collections.Concurrent;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorderRegistry
{
    bool TryRegister(string channelId);
    void Register(string channelId, IStreamRecorder recorder, Task backgroundTask);
    bool TryGet(string channelId, out IStreamRecorder recorder);
    void Remove(string channelId);
    Task[] GetAllBackgroundTasks();
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
}