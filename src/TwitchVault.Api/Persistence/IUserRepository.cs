using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByIdAsync(Guid id);
    Task AddAsync(User user);
}