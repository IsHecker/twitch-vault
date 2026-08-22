using System.Collections.Concurrent;

namespace TwitchVault.Api.Endpoints.Testing;

/// <summary>
/// Singleton in-memory state shared across all live-test endpoints.
/// Tracks which channel IDs were added by the /testing/live/start endpoint
/// and which user kicked off the session, so stop-all and delete-all operate
/// only on what this session created.
/// </summary>
public sealed class LiveTestSession
{
    private readonly ConcurrentDictionary<string, string> _channelIdToName = new();
    private Guid _sessionUserId;

    public Guid SessionUserId => _sessionUserId;
    public IReadOnlyCollection<string> ChannelIds => (IReadOnlyCollection<string>)_channelIdToName.Keys;
    public bool HasActiveSession => !_channelIdToName.IsEmpty;

    public void BeginSession(Guid userId)
    {
        _sessionUserId = userId;
    }

    public void Track(string channelId, string channelName) =>
        _channelIdToName[channelId] = channelName;

    public string? GetName(string channelId) =>
        _channelIdToName.TryGetValue(channelId, out var name) ? name : null;

    public IReadOnlyDictionary<string, string> GetSnapshot() =>
        _channelIdToName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public void Clear() => _channelIdToName.Clear();
}
