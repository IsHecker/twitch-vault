using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Common.Extensions;

public static class PaginationExtensions
{
    public static IQueryable<T> Paginate<T>(this IQueryable<T> query, Pagination pagination)
    {
        return query.Skip(pagination.PageSize * (pagination.PageNumber - 1)).Take(pagination.PageSize);
    }

    public static IEnumerable<T> Paginate<T>(this IEnumerable<T> query, Pagination pagination)
    {
        return query.Skip(pagination.PageSize * (pagination.PageNumber - 1)).Take(pagination.PageSize);
    }

    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> source,
        Pagination pagination)
    {
        var items = await source.Paginate(pagination).ToListAsync();
        var totalCount = await source.CountAsync();

        return new PagedResponse<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    public static PagedResponse<T> ToPagedResponse<T>(
        this IEnumerable<T> source,
        Pagination pagination)
    {
        var list = source.ToList();
        var items = list.Paginate(pagination);

        return new PagedResponse<T>
        {
            Items = items,
            TotalCount = list.Count,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }
}