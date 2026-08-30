namespace TwitchVault.Api.Common;

public readonly record struct Pagination(int PageNumber = 1, int PageSize = 5)
{
    public static readonly Pagination Default = new();
}