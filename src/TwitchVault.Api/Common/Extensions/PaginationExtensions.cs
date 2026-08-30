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
        this IEnumerable<T> source,
        Pagination pagination,
        int totalCount)
    {
        var items = await ((IQueryable<T>)source).ToListAsync();

        return new PagedResponse<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }
}