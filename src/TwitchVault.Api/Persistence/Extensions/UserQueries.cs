using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Extensions;

public static class UserQueries
{
    public static Task<User?> GetByIdAsync(this IQueryable<User> query, Guid id) =>
        query.FirstOrDefaultAsync(u => u.Id == id);

    public static Task<User?> GetByUsernameAsync(this IQueryable<User> query, string username) =>
        query.FirstOrDefaultAsync(u => u.Username == username);
}