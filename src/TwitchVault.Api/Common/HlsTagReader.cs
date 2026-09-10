namespace TwitchVault.Api.Common;

public static class HlsTagReader
{
    public static string ReadTagValue(ReadOnlySpan<char> manifest, ReadOnlySpan<char> tagName, char endChar = '\n')
    {
        var span = ReadTagValueSpan(manifest, tagName, endChar);
        return span.IsEmpty ? string.Empty : span.ToString();
    }

    public static ReadOnlySpan<char> ReadTagValueSpan(ReadOnlySpan<char> manifest, ReadOnlySpan<char> tagName, char endChar = '\n')
    {
        ReadOnlySpan<char> ValidSeparators = [':', '=', '\n', '\r'];

        var offset = 0;
        while (offset < manifest.Length)
        {
            var startTagIndex = manifest[offset..].IndexOf(tagName, StringComparison.Ordinal);
            if (startTagIndex < 0)
                return [];

            startTagIndex += offset;

            var afterTagIndex = startTagIndex + tagName.Length;
            if (afterTagIndex >= manifest.Length)
                return [];

            if (startTagIndex > 0)
            {
                var prev = manifest[startTagIndex - 1];
                if (char.IsLetterOrDigit(prev))
                    continue;
            }

            var next = manifest[afterTagIndex];
            if (!ValidSeparators.Contains(next))
            {
                offset = startTagIndex + 1;
                continue;
            }

            if (next is '\n' or '\r')
                return [];

            var startIndex = afterTagIndex + 1;
            var remaining = manifest[startIndex..];
            var endIndex = remaining.IndexOf(endChar);
            if (endIndex < 0)
                return remaining.Trim();

            return remaining[..endIndex].Trim();
        }

        return [];
    }
}