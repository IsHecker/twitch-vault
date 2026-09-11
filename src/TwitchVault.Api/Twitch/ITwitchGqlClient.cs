namespace TwitchVault.Api.Twitch;

public interface ITwitchGqlClient
{
    Task<Dictionary<Domain.Channel, bool>> IsChannelLiveAsync(List<Domain.Channel> channels, CancellationToken cancellationToken);
    Task<StreamMetadata?> GetStreamMetadataAsync(string channel, CancellationToken cancellationToken);
    Task<string> GetMasterPlaylistAsync(string channel, CancellationToken cancellationToken);
    Task<ResponseStream> GetPlaylistContentAsync(string playlistUrl, CancellationToken cancellationToken);
    Task<ResponseStream> DownloadAsStreamAsync(string url, CancellationToken cancellationToken);
    Task<string?> GetStreamVODIdAsync(string channel, CancellationToken cancellationToken);
    Task<string?> GetChannelIdAsync(string channel, CancellationToken cancellationToken);
}