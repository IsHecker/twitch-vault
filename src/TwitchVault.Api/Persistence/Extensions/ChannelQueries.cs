using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Extensions;

public static class ChannelQueries
{
    public static Task<Channel?> GetByIdAsync(this IQueryable<Channel> query, string id, CancellationToken ct = default) =>
        query.FirstOrDefaultAsync(c => c.Id == id, ct);

    public static Task<Channel?> GetByNameAsync(this IQueryable<Channel> query, string name, CancellationToken ct = default) =>
        query.FirstOrDefaultAsync(c => c.Name == name, ct);

    public static IQueryable<Channel> ById(this IQueryable<Channel> query, string id) =>
        query.Where(c => c.Id == id);

    public static IQueryable<Channel> Monitored(this IQueryable<Channel> query) =>
        query.Where(c => c.IsArchived);

    public static IQueryable<Channel> Live(this IQueryable<Channel> query) =>
        query.Where(c => c.IsLive);

    public static IQueryable<Channel> Offline(this IQueryable<Channel> query) =>
        query.Where(c => !c.IsLive);

    public static IQueryable<UserChannel> ForUser(this IQueryable<UserChannel> query, Guid userId) =>
        query.Where(uc => uc.UserId == userId);

    public static IQueryable<UserChannel> ForChannel(this IQueryable<UserChannel> query, string channelId) =>
        query.Where(uc => uc.ChannelId == channelId);


    public static Task SetLiveAsync(this IQueryable<Channel> query, bool isLive) =>
        query.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsLive, isLive));
}