namespace TwitchVault.Api.Persistence;

public interface IStreamRepository
{
    Task<List<Domain.Stream>> GetStreamsByChannelIdAsync(string channelId);
    Task<Domain.Stream?> GetStreamByIdAsync(string twitchStreamId);
    Task AddStreamAsync(Domain.Stream stream);
    Task UpdateAsync(Domain.Stream stream);
    Task DeleteStreamAsync(string twitchStreamId);
}