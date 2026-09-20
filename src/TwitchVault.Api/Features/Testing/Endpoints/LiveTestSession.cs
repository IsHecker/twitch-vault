using System.Collections.Concurrent;

namespace TwitchVault.Api.Features.Testing.Endpoints;

/// <summary>
/// Singleton in-memory state shared across all live-test endpoints.
/// Tracks which channel IDs were added by the /testing/live/start endpoint
/// and which user kicked off the session, so stop-all and delete-all operate
/// only on what this session created.
/// </summary>
public sealed class LiveTestSession
{
    private readonly ConcurrentDictionary<string, (string Name, bool IsNewChannel)> _channels = new();
    private Guid _sessionUserId;

    public Guid SessionUserId => _sessionUserId;
    public IReadOnlyCollection<string> ChannelIds => (IReadOnlyCollection<string>)_channels.Keys;
    public bool HasActiveSession => !_channels.IsEmpty;

    public void BeginSession(Guid userId)
    {
        _sessionUserId = userId;
    }

    public void Track(string channelId, string channelName, bool isNewChannel = true) =>
        _channels[channelId] = (channelName, isNewChannel);

    public string? GetName(string channelId) =>
        _channels.TryGetValue(channelId, out var info) ? info.Name : null;

    public bool IsNewChannel(string channelId) =>
        _channels.TryGetValue(channelId, out var info) && info.IsNewChannel;

    public IReadOnlyDictionary<string, string> GetSnapshot() =>
        _channels.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

    public IReadOnlyList<(string ChannelId, string ChannelName)> GetCreatedChannels() =>
        _channels.Where(kvp => kvp.Value.IsNewChannel)
                 .Select(kvp => (kvp.Key, kvp.Value.Name))
                 .ToList();

    public IReadOnlyList<(string ChannelId, string ChannelName)> GetLinkedChannels() =>
        _channels.Where(kvp => !kvp.Value.IsNewChannel)
                .Select(kvp => (kvp.Key, kvp.Value.Name))
                .ToList();

    public void Clear() => _channels.Clear();
}