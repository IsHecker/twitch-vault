using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Auth;

public static class UserQueries
{
    public static Task<User?> GetByIdAsync(this IQueryable<User> query, Guid id) =>
        query.FirstOrDefaultAsync(u => u.Id == id);

    public static Task<User?> GetByGoogleIdAsync(this IQueryable<User> query, string googleId) =>
        query.FirstOrDefaultAsync(u => u.GoogleId == googleId);

    public static Task<User?> GetByUsernameAsync(this IQueryable<User> query, string username) =>
        query.FirstOrDefaultAsync(u => u.Username == username);
}