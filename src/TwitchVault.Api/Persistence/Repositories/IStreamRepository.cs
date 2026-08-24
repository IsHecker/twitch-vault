namespace TwitchVault.Api.Persistence.Repositories;

public interface IStreamRepository
{
    Task<IEnumerable<Domain.Stream>> GetAllAsync();
    Task<IEnumerable<Domain.Stream>> ListByChannelIdAsync(string channelId);
    Task<Domain.Stream?> GetByIdAsync(string twitchStreamId);
    Task AddAsync(Domain.Stream stream);
    Task UpdateAsync(Domain.Stream stream);
    Task DeleteAsync(string twitchStreamId);
}