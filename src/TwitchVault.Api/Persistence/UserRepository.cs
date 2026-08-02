using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public class UserRepository(JsonDatabase db) : IUserRepository
{
    public async Task<List<User>> GetAllAsync()
    {
        var data = await db.ReadAsync();
        return data.Users;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var data = await db.ReadAsync();
        return data.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        var data = await db.ReadAsync();
        return data.Users.FirstOrDefault(u => u.Id == id);
    }

    public Task AddAsync(User user) =>
        db.WriteAsync(data => data.Users.Add(user));
}