namespace TwitchVault.Api.Persistence;

public interface IStreamRepository
{
    Task<List<Domain.Stream>> GetStreamsByChannelIdAsync(string channelId);
    Task<Domain.Stream?> GetStreamByIdAsync(string twitchStreamId);
    Task AddAsync(Domain.Stream stream);
    Task UpdateAsync(Domain.Stream stream);
    Task DeleteStreamAsync(string twitchStreamId);
}