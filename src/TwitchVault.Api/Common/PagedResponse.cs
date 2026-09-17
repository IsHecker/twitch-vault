namespace TwitchVault.Api.Common;

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; init; } = null!;
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PagedResponse<TResult> Map<TResult>(Func<T, TResult> selector) =>
        new()
        {
            Items = Items.Select(selector),
            TotalCount = TotalCount,
            PageNumber = PageNumber,
            PageSize = PageSize
        };
}