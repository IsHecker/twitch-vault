namespace TwitchVault.Api.Features.Twitch;

public enum VodAccessibility
{
    NotFound,
    Public,
    SubscriberOnly
}

public interface ITwitchGqlClient
{
    Task<Dictionary<Channel, bool>> IsChannelLiveAsync(List<Channel> channels, CancellationToken cancellationToken);
    Task<StreamMetadata?> GetStreamMetadataAsync(string channel, CancellationToken cancellationToken);
    Task<string> GetMasterPlaylistAsync(string channel, CancellationToken cancellationToken);
    Task<ResponseStream> GetPlaylistContentAsync(string playlistUrl, CancellationToken cancellationToken);
    Task<ResponseStream> DownloadAsStreamAsync(string url, CancellationToken cancellationToken);
    Task<string?> GetStreamVODIdAsync(string channel, CancellationToken cancellationToken);
    Task<string?> GetChannelIdAsync(string channel, CancellationToken cancellationToken);
    Task<VodAccessibility> GetVodAccessibilityAsync(string vodId, CancellationToken cancellationToken);
}