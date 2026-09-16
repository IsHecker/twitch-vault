namespace TwitchVault.Api.Common;


public static class HlsTagReader
{
    private const char NewLine = '\n';
    private const char CarriageReturn = '\r';
    private delegate ReadOnlySpan<T> TrimFunc<T>(ReadOnlySpan<T> span);

    public static string ReadTagValue(ReadOnlySpan<char> manifest, ReadOnlySpan<char> tagName, char endChar = NewLine)
    {
        var span = ReadTagValueSpan(manifest, tagName, endChar);
        return span.IsEmpty ? string.Empty : span.ToString();
    }

    public static ReadOnlySpan<char> ReadTagValueSpan(ReadOnlySpan<char> manifest, ReadOnlySpan<char> tagName, char endChar = NewLine) =>
        ReadTagValueSpanCore(manifest, tagName, endChar,
            [':', '=', NewLine, CarriageReturn], NewLine, CarriageReturn,
            static c => char.IsLetterOrDigit(c),
            static s => s.Trim());

    public static ReadOnlySpan<byte> ReadTagValueSpan(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> tagName, byte endChar = (byte)NewLine)
    {
        ReadOnlySpan<byte> validSeparators = [(byte)':', (byte)'=', (byte)NewLine, (byte)CarriageReturn];

        return ReadTagValueSpanCore(manifest, tagName, endChar,
            validSeparators, (byte)NewLine, (byte)CarriageReturn,
            IsAsciiLetterOrDigit,
            static s => s.Trim((byte)' ').Trim((byte)'\t'));
    }

    private static ReadOnlySpan<T> ReadTagValueSpanCore<T>(
        ReadOnlySpan<T> manifest, ReadOnlySpan<T> tagName, T endChar,
        ReadOnlySpan<T> validSeparators, T newLine, T carriageReturn,
        Func<T, bool> isLetterOrDigit, TrimFunc<T> trim)
        where T : IEquatable<T>
    {
        var offset = 0;
        while (offset < manifest.Length)
        {
            var startTagIndex = manifest[offset..].IndexOf(tagName);
            if (startTagIndex < 0)
                return [];

            startTagIndex += offset;

            var afterTagIndex = startTagIndex + tagName.Length;
            if (afterTagIndex >= manifest.Length)
                return [];

            if (startTagIndex > 0 && isLetterOrDigit(manifest[startTagIndex - 1]))
            {
                offset = startTagIndex + 1;
                continue;
            }

            var next = manifest[afterTagIndex];
            if (!validSeparators.Contains(next))
            {
                offset = startTagIndex + 1;
                continue;
            }

            if (next.Equals(newLine) || next.Equals(carriageReturn))
                return [];

            var remaining = manifest[(afterTagIndex + 1)..];
            var endIndex = remaining.IndexOf(endChar);
            var value = endIndex < 0 ? remaining : remaining[..endIndex];

            return trim(value);
        }

        return [];
    }

    private static bool IsAsciiLetterOrDigit(byte b) =>
        b is (>= (byte)'0' and <= (byte)'9')
          or (>= (byte)'a' and <= (byte)'z')
          or (>= (byte)'A' and <= (byte)'Z');
}