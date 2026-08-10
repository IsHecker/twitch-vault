using System.Text;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Discord;

public class HlsPlaylistRewriter(string localPlaylistPath, string outputPath)
{
    private readonly IEnumerator<string> _lines = File.ReadLines(localPlaylistPath).GetEnumerator();
    private readonly FileStream _output = new(
        outputPath,
        FileMode.OpenOrCreate,
        FileAccess.Write,
        FileShare.ReadWrite,
        bufferSize: 4096,
        useAsync: true);

    public async Task RewriteNextSegmentAsync(
        string newSegmentName,
        CancellationToken cancellationToken)
    {
        while (_lines.MoveNext())
        {
            var line = _lines.Current;

            if (line.Contains(HlsTags.MapPrefix))
            {
                await WriteLineAsync(HlsTags.Map(newSegmentName), cancellationToken);
                await _output.FlushAsync(cancellationToken);
                return;
            }

            if (line.Contains(HlsSegmentNaming.SegmentPrefix))
            {
                await WriteLineAsync(newSegmentName, cancellationToken);
                await _output.FlushAsync(cancellationToken);
                return;
            }

            await WriteLineAsync(line, cancellationToken);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        while (_lines.MoveNext())
            await WriteLineAsync(_lines.Current, cancellationToken);

        await _output.FlushAsync(cancellationToken);

        _lines.Dispose();
        await _output.DisposeAsync();
    }

    private ValueTask WriteLineAsync(string text, CancellationToken ct = default)
        => _output.WriteAsync(Encoding.UTF8.GetBytes(text + '\n'), ct);
}