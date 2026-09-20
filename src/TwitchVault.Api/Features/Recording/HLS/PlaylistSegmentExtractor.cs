using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Text;

namespace TwitchVault.Api.Features.Recording.HLS;

public readonly record struct RemoteSegment(string Url, float Duration, bool IsInitSegment = false);

public record struct PlaylistExtractionResult(
    IReadOnlyList<RemoteSegment> Segments,
    long LastMediaSequence,
    bool IsStreamEnded);

public static class PlaylistSegmentExtractor
{
    public static async ValueTask<PlaylistExtractionResult> ExtractNewSegmentsAsync(
        ResponseStream playlistStream, long lastMediaSequence, CancellationToken ct)
    {
        await using var _ = playlistStream;
        PipeReader reader = PipeReader.Create(playlistStream.Content);
        var state = new ParsingState(lastMediaSequence);

        while (true)
        {
            ReadResult readResult = await reader.ReadAsync(ct);
            SequencePosition consumedUpTo = ParseAvailableLines(readResult.Buffer, ref state, readResult.IsCompleted);
            reader.AdvanceTo(consumedUpTo, readResult.Buffer.End);

            if (readResult.IsCompleted)
                break;
        }

        if (!state.FoundMediaSequenceTag)
            return new PlaylistExtractionResult([], lastMediaSequence, IsStreamEnded: state.IsStreamEnded);

        return new PlaylistExtractionResult(
            state.Segments ?? (IReadOnlyList<RemoteSegment>)[],
            state.CurrentSequence,
            state.IsStreamEnded);
    }

    private static SequencePosition ParseAvailableLines(
        ReadOnlySequence<byte> buffer, ref ParsingState state, bool isCompleted)
    {
        var lineSplitter = new SequenceReader<byte>(buffer);

        while (lineSplitter.TryReadTo(out ReadOnlySequence<byte> lineBytes, (byte)'\n', advancePastDelimiter: true))
        {
            var line = lineBytes.IsSingleSegment
                ? lineBytes.FirstSpan : lineBytes.ToArray();

            ProcessLine(line, ref state);
        }

        if (isCompleted && lineSplitter.Remaining > 0)
        {
            var remainder = lineSplitter.UnreadSequence;
            var line = remainder.IsSingleSegment
                ? remainder.FirstSpan : remainder.ToArray();

            ProcessLine(line, ref state);
            lineSplitter.AdvanceToEnd();
        }

        return lineSplitter.Position;
    }

    // private static ReadOnlySpan<byte> TrimTrailingCr(ReadOnlySpan<byte> line)
    // {
    //     // Handles CRLF playlists without allocating.
    //     if (!line.IsEmpty && line[^1] == (byte)'\r')
    //         line = line[..^1];
    //     return line;
    // }

    private static void ProcessLine(ReadOnlySpan<byte> line, ref ParsingState state)
    {
        if (line.IsEmpty)
            return;

        if (line.StartsWith(HlsTags.Utf8.EndList))
        {
            state.IsStreamEnded = true;
            return;
        }

        if (!state.FoundMediaSequenceTag)
        {
            var sequenceSpan = HlsTagReader.ReadTagValueSpan(line, HlsTags.Utf8.MediaSequencePrefix);
            if (Utf8Parser.TryParse(sequenceSpan, out long firstSequence, out _))
            {
                state.CurrentSequence = firstSequence - 1;
                state.FoundMediaSequenceTag = true;
                return;
            }

            if (!line.StartsWith(HlsTags.Utf8.ExtInfPrefix) && !line.StartsWith(HlsTags.Utf8.MapPrefix))
                return;
        }

        state.Segments ??= [];
        if (state.WaitingForSegmentUrl)
        {
            state.Segments.Add(new RemoteSegment(Encoding.UTF8.GetString(line), state.PendingDuration));
            state.WaitingForSegmentUrl = false;
            return;
        }

        if (line.StartsWith(HlsTags.Utf8.MapPrefix))
        {
            var initUrlBytes = HlsTagReader.ReadTagValueSpan(line, HlsTags.Utf8.MapPrefix).Trim((byte)'"');
            state.Segments.Add(new RemoteSegment(Encoding.UTF8.GetString(initUrlBytes), 0, IsInitSegment: true));
            return;
        }

        if (!line.StartsWith(HlsTags.Utf8.ExtInfPrefix))
            return;

        if (++state.CurrentSequence <= state.LastMediaSequence)
            return;

        var durationSpan = HlsTagReader.ReadTagValueSpan(line, HlsTags.Utf8.ExtInfPrefix, (byte)',');
        _ = Utf8Parser.TryParse(durationSpan, out float duration, out _);

        state.PendingDuration = duration;
        state.WaitingForSegmentUrl = true;
    }

    private struct ParsingState(long lastMediaSequence)
    {
        public readonly long LastMediaSequence = lastMediaSequence;
        public long CurrentSequence;
        public bool FoundMediaSequenceTag;
        public bool IsStreamEnded;
        public List<RemoteSegment>? Segments;
        public bool WaitingForSegmentUrl;
        public float PendingDuration;
    }
}