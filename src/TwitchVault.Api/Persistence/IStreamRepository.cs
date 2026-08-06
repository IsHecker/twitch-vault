namespace TwitchVault.Api.Persistence;

public interface IStreamRepository
{
    Task<List<Domain.Stream>> GetAllAsync();
    Task<List<Domain.Stream>> ListByChannelIdAsync(string channelId);
    Task<Domain.Stream?> GetByIdAsync(string twitchStreamId);
    Task AddAsync(Domain.Stream stream);
    Task UpdateAsync(Domain.Stream stream);
    Task DeleteAsync(string twitchStreamId);
}