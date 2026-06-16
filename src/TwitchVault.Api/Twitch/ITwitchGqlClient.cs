namespace TwitchVault.Api.Twitch;

public interface ITwitchGqlClient
{
    Task<StreamMetadata?> GetStreamMetadataAsync(string channel, CancellationToken cancellationToken);
    Task<Dictionary<Domain.Channel, StreamMetadata?>> GetStreamMetadataAsync(List<Domain.Channel> channels, CancellationToken cancellationToken);
    Task<string> GetMasterPlaylistAsync(string channel, CancellationToken cancellationToken);
    Task<string> GetPlaylistContentAsync(string playlistUrl, CancellationToken cancellationToken);
    Task<Stream> DownloadAsStreamAsync(string url, CancellationToken cancellationToken);
    Task<string?> GetStreamVODIdAsync(string channel, CancellationToken cancellationToken);
    Task<string?> GetVODThumbnailUrlAsync(string vodId, CancellationToken cancellationToken);
    Task<string?> GetChannelIdAsync(string channel, CancellationToken cancellationToken);
}