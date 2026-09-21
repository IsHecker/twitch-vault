using System.Text.RegularExpressions;

namespace TwitchVault.Api.Features.Channels;

public static partial class TwitchLogin
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_]{2,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex LoginPattern();

    public static bool TryNormalize(string? input, out string login)
    {
        login = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var candidate = input.Trim().ToLowerInvariant();
        if (!LoginPattern().IsMatch(candidate))
            return false;

        login = candidate;
        return true;
    }
}