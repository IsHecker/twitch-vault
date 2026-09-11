using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public readonly record struct RemoteSegment(string Url, float Duration, bool IsInitSegment = false);

public record struct PlaylistExtractionResult(
    IReadOnlyList<RemoteSegment> Segments,
    long LastMediaSequence,
    bool IsStreamEnded);

public static class PlaylistSegmentExtractor
{
    public static async ValueTask<PlaylistExtractionResult> ExtractNewSegmentsAsync(
        Stream playlistStream, long lastMediaSequence, CancellationToken ct)
    {
        PipeReader reader = PipeReader.Create(playlistStream);
        var state = new ParsingState(lastMediaSequence);

        while (true)
        {
            ReadResult readResult = await reader.ReadAsync(ct);

            // consumedUpTo = how far into this chunk we managed to fully parse.
            // Anything after that point is an incomplete line and gets kept
            // for next time automatically by AdvanceTo below.
            SequencePosition consumedUpTo = ParseAvailableLines(readResult.Buffer, ref state);

            // Tell the pipe: "the bytes up to consumedUpTo are done, you can
            // reuse that memory." This is what keeps memory usage low.
            reader.AdvanceTo(consumedUpTo, readResult.Buffer.End);

            if (readResult.IsCompleted)
                break;
        }

        if (!state.FoundMediaSequenceTag)
            return new PlaylistExtractionResult([], lastMediaSequence, IsStreamEnded: false);

        return new PlaylistExtractionResult(
            state.Segments ?? (IReadOnlyList<RemoteSegment>)[],
            state.CurrentSequence,
            state.IsStreamEnded);
    }

    private static SequencePosition ParseAvailableLines(ReadOnlySequence<byte> buffer, ref ParsingState state)
    {
        var lineSplitter = new SequenceReader<byte>(buffer);

        while (lineSplitter.TryReadTo(out ReadOnlySequence<byte> lineBytes, (byte)'\n', advancePastDelimiter: true))
        {
            // Almost always true: the line fits in one pooled buffer.
            var line = lineBytes.IsSingleSegment
                ? lineBytes.FirstSpan : lineBytes.ToArray(); // rare: line happened to straddle two buffers

            if (line.IsEmpty)
                continue;

            ProcessLine(line, ref state);
        }

        // Position = everything fully consumed above. Whatever's left after
        // this (a line cut off mid-way) stays in the pipe for next time.
        return lineSplitter.Position;
    }

    private static void ProcessLine(ReadOnlySpan<byte> line, ref ParsingState state)
    {
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

        if (line.StartsWith(HlsTags.Utf8.EndList))
        {
            state.IsStreamEnded = true;
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

    public static IEnumerable<RemoteSegment> EnumerateSegments(IEnumerable<string> lines)
    {
        float? pendingDuration = null;
        foreach (var line in lines)
        {
            var trimmed = line.AsSpan().Trim();
            if (trimmed.IsEmpty)
                continue;

            if (trimmed.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
            {
                var initUrl = HlsTagReader.ReadTagValueSpan(trimmed, HlsTags.MapPrefix).Trim('"').ToString();
                yield return new RemoteSegment(initUrl, 0, IsInitSegment: true);
                continue;
            }

            if (trimmed.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
            {
                var durationSpan = HlsTagReader.ReadTagValueSpan(trimmed, HlsTags.ExtInfPrefix, ',');
                pendingDuration = float.Parse(durationSpan, CultureInfo.InvariantCulture);
                continue;
            }

            if (pendingDuration is not null)
            {
                yield return new RemoteSegment(trimmed.ToString(), pendingDuration.Value);
                pendingDuration = null;
            }
        }
    }
}
// 284