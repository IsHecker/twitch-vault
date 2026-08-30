using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Extensions;

public static class UserQueries
{
    public static Task<User?> GetByUsernameAsync(this IQueryable<User> query, string username) =>
        query.FirstOrDefaultAsync(u => u.Username == username);
}