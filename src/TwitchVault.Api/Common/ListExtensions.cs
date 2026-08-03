namespace TwitchVault.Api.Common;

public static class ListExtensions
{
    public static IEnumerable<T> ReverseIterator<T>(this IList<T> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            yield return list[i];
    }
}