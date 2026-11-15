using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vortice.MediaFoundation;

namespace TwitchVault.Api.Features.Recording.Storyboard;

public sealed class FrameExtractor(ILogger<FrameExtractor> logger) : IFrameExtractor
{
    // HLS .ts segments are expected to have one keyframe every 2 seconds.
    private const int KeyframeIntervalSeconds = 2;

    private readonly byte[] _thumbBuffer = new byte[
        Math.Max(
            StoryboardConstants.ThumbWidth * StoryboardConstants.ThumbHeight,
            StoryboardConstants.StripFrameWidth * StoryboardConstants.StripFrameHeight) * 3];
    private System.IO.Stream? _initStream;

    // Holds the .ts segment being scanned. Grows on demand and is reused for every frame of a compilation.
    // Not thread-safe, same as _thumbBuffer: one extractor instance per compilation.
    private byte[] _segmentBuffer = [];

    public void Reset()
    {
        _initStream?.Dispose();
        _initStream = null;

        // Don't keep a multi-MB buffer alive between compilations.
        _segmentBuffer = [];
    }

    /// <summary>
    /// Decodes the frame at <paramref name="targetSecond"/>, scales it to dstWidth x dstHeight and writes it
    /// into <paramref name="destination"/> at (destX, destY). Returns false and leaves destination untouched
    /// if no frame could be produced.
    /// </summary>
    public bool ExtractFrameInto(
        string filePath, int targetSecond, int dstWidth, int dstHeight,
        Image<Bgr24> destination, int destX, int destY)
    {
        if (destX < 0 || destY < 0
            || destX + dstWidth > destination.Width
            || destY + dstHeight > destination.Height)
            throw new ArgumentOutOfRangeException(nameof(destination), "Target rectangle does not fit the destination image.");

        if (!ExtractFrameCore(filePath, targetSecond, dstWidth, dstHeight))
            return false;

        var rowBytes = dstWidth * 3;
        destination.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < dstHeight; y++)
            {
                var src = MemoryMarshal.Cast<byte, Bgr24>(_thumbBuffer.AsSpan(y * rowBytes, rowBytes));
                src.CopyTo(accessor.GetRowSpan(destY + y).Slice(destX, dstWidth));
            }
        });

        return true;
    }

    /// <summary>Decodes and scales one frame into <see cref="_thumbBuffer"/>. Returns false if none was produced.</summary>
    private bool ExtractFrameCore(string filePath, int targetSecond, int dstWidth, int dstHeight)
    {
        try
        {
            // ── TS path ────────────────────────────────────────────────────────────
            if (filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                var segment = TsSegment.Load(filePath, ref _segmentBuffer);

                // Only the keyframe at the target index is needed, so stop scanning once it is found.
                // If the segment has fewer keyframes, the last one found is used.
                var wantedCount = Math.Max(0, targetSecond / KeyframeIntervalSeconds) + 1;
                var offsets = segment.FindKeyframeOffsets(wantedCount);

                return offsets.Count != 0
                    && ExtractFirstFrame(segment, offsets[^1], dstWidth, dstHeight);
            }

            // ── fMP4 path ──────────────────────────────────────────────────────────
            using var stream = OpenSegmentStream(filePath);

            using var readerAttributes = MediaFactory.MFCreateAttributes(1);
            readerAttributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);

            using var reader = MediaFactory.MFCreateSourceReaderFromByteStream(stream, readerAttributes);
            var output = ConfigureVideoOutput(reader);

            using var firstSample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out SourceReaderFlag firstFlags,
                out _);

            if (firstSample is null || firstFlags.HasFlag(SourceReaderFlag.EndOfStream))
                return false;

            if (targetSecond <= 0)
                return FillThumbBuffer(firstSample, output, dstWidth, dstHeight);

            var targetTicks = TimeSpan.FromSeconds(targetSecond).Ticks;
            var offset = targetSecond % 2 == 0 ? targetTicks + TimeSpan.FromSeconds(1).Ticks : targetTicks;

            reader.SetCurrentPosition(firstSample.SampleTime + offset);

            using var sample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out SourceReaderFlag seekFlags,
                out _);

            if (sample is not null
                && !seekFlags.HasFlag(SourceReaderFlag.EndOfStream)
                && FillThumbBuffer(sample, output, dstWidth, dstHeight))
                return true;

            return FillThumbBuffer(firstSample, output, dstWidth, dstHeight);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract frames via Media Foundation from '{FilePath}'.", filePath);
        }

        return false;
    }

    private System.IO.Stream OpenSegmentStream(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        var initPath = Path.Combine(directory!, "init.mp4");
        if (!File.Exists(initPath))
            return File.OpenRead(filePath);

        _initStream ??= new MemoryStream(File.ReadAllBytes(initPath), writable: false);
        _initStream.Position = 0;
        return new CompositeStream(_initStream, File.OpenRead(filePath));
    }

    private static VideoOutput ConfigureVideoOutput(IMFSourceReader reader)
    {
        using var requestedType = MediaFactory.MFCreateMediaType();
        requestedType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        requestedType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);

        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requestedType);

        using var negotiatedType = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);

        MediaFactory.MFGetAttributeSize(
            negotiatedType,
            MediaTypeAttributeKeys.FrameSize,
            out uint width,
            out uint height);

        return new VideoOutput((int)width, (int)height);
    }

    /// <summary>Box-averages the decoded RGB32 frame down to dstWidth x dstHeight into <see cref="_thumbBuffer"/> (BGR24).</summary>
    private unsafe bool FillThumbBuffer(
        IMFSample? sample,
        VideoOutput output,
        int dstWidth,
        int dstHeight)
    {
        if (sample is null)
            return false;

        using var buffer = sample.ConvertToContiguousBuffer();
        if (buffer is null)
            return false;

        buffer.Lock(out nint pointer, out _, out int currentLength);

        try
        {
            var src = (byte*)pointer;
            var srcWidth = output.Width;
            var srcHeight = output.Height;

            var codedHeight = (srcHeight + 15) / 16 * 16;
            var stride = currentLength / codedHeight;

            Span<int> xStarts = stackalloc int[dstWidth];
            Span<int> xEnds = stackalloc int[dstWidth];

            for (var x = 0; x < dstWidth; x++)
            {
                xStarts[x] = x * srcWidth / dstWidth;
                var end = (x + 1) * srcWidth / dstWidth;
                if (end <= xStarts[x]) end = xStarts[x] + 1;
                if (end > srcWidth) end = srcWidth;
                xEnds[x] = end;
            }

            fixed (byte* dstStart = _thumbBuffer)
            {
                var dst = dstStart;

                for (var yd = 0; yd < dstHeight; yd++)
                {
                    var ysStart = yd * srcHeight / dstHeight;
                    var ysEnd = (yd + 1) * srcHeight / dstHeight;
                    if (ysEnd <= ysStart) ysEnd = ysStart + 1;
                    if (ysEnd > srcHeight) ysEnd = srcHeight;
                    var yCount = ysEnd - ysStart;

                    for (var xd = 0; xd < dstWidth; xd++)
                    {
                        var xsStart = xStarts[xd];
                        var xsEnd = xEnds[xd];
                        var count = yCount * (xsEnd - xsStart);

                        int sumB = 0, sumG = 0, sumR = 0;

                        for (var ys = ysStart; ys < ysEnd; ys++)
                        {
                            var row = src + ys * stride + xsStart * 4;
                            for (var xs = xsStart; xs < xsEnd; xs++)
                            {
                                sumB += row[0];
                                sumG += row[1];
                                sumR += row[2];
                                row += 4;
                            }
                        }

                        dst[0] = (byte)(sumB / count);
                        dst[1] = (byte)(sumG / count);
                        dst[2] = (byte)(sumR / count);
                        dst += 3;
                    }
                }
            }

            return true;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    /// <summary>Decodes the first video frame of a TS segment, starting at the given byte offset.</summary>
    private bool ExtractFirstFrame(TsSegment segment, int startOffset, int dstWidth, int dstHeight)
    {
        try
        {
            using var stream = segment.OpenFrom(startOffset);

            using var readerAttributes = MediaFactory.MFCreateAttributes(1);
            readerAttributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);

            using var reader = MediaFactory.MFCreateSourceReaderFromByteStream(stream, readerAttributes);
            var output = ConfigureVideoOutput(reader);

            while (true)
            {
                using var sample = reader.ReadSample(
                    SourceReaderIndex.FirstVideoStream,
                    SourceReaderControlFlag.None,
                    out _,
                    out SourceReaderFlag flags,
                    out _);

                if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    break;

                if (sample is null)
                    continue;

                return FillThumbBuffer(sample, output, dstWidth, dstHeight);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract keyframe from TS stream at offset {Offset}.", startOffset);
        }

        return false;
    }

    private readonly record struct VideoOutput(int Width, int Height);
}

/// <summary>
/// A view over one .ts segment loaded into a caller-owned buffer. It owns nothing and needs no disposal,
/// but it is only valid until the next <see cref="Load"/> into the same buffer.
/// </summary>
internal readonly struct TsSegment
{
    private const int PatPid = 0;

    // PAT packets closer together than this belong to the same GOP.
    private const int MinGopSizeBytes = 20_000;

    // How many packets after a PAT are searched for a keyframe marker.
    private const int KeyframeLookaheadPackets = 15;

    // H.264 NAL unit types that start a keyframe.
    private const int IdrSliceNalType = 5;
    private const int SpsNalType = 7;
    private const int NalTypeMask = 0x1F;

    private static ReadOnlySpan<byte> StartCodePrefix => [0, 0, 1];

    // Buffer capacity is rounded up to whole MiB so slightly larger segments don't reallocate every time.
    private const int CapacityStep = 1024 * 1024;

    private readonly byte[]? _buffer;
    private readonly int _length;

    private TsSegment(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the whole file into <paramref name="buffer"/>, growing it (never shrinking it) if the file
    /// doesn't fit. The caller keeps the buffer and passes the same one to the next call.
    /// </summary>
    public static TsSegment Load(string path, ref byte[] buffer)
    {
        using var file = File.OpenRead(path);

        var length = checked((int)file.Length);
        EnsureCapacity(ref buffer, length);

        file.ReadExactly(buffer, 0, length);
        return new TsSegment(buffer, length);
    }

    private static void EnsureCapacity(ref byte[] buffer, int length)
    {
        if (buffer.Length >= length)
            return;

        // 25% headroom, rounded up to whole MiB.
        var rounded = ((long)length * 5 / 4 + CapacityStep - 1) / CapacityStep * CapacityStep;
        var capacity = Math.Max(length, (int)Math.Min(Array.MaxLength, rounded));

        // Contents are overwritten by the read, so skip zeroing.
        buffer = GC.AllocateUninitializedArray<byte>(capacity);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>A read-only stream over the segment starting at <paramref name="offset"/>.</summary>
    public System.IO.Stream OpenFrom(int offset) =>
        new MemoryStream(Buffer, offset, _length - offset, writable: false);

    /// <summary>
    /// Returns the byte offsets where GOPs start, in file order. Stops scanning once
    /// <paramref name="maxCount"/> offsets are found. The first PAT always counts; if no PAT is
    /// found at all, the list is [0]. Segments shorter than one packet return an empty list.
    /// </summary>
    public List<int> FindKeyframeOffsets(int maxCount = int.MaxValue)
    {
        var data = Data;
        var offsets = new List<int>();
        if (data.Length < TsPacket.Size)
            return offsets;

        var lastOffset = -1;
        var position = 0;

        while (position + TsPacket.Size <= data.Length)
        {
            // Lost sync: slide forward one byte at a time until a sync byte shows up.
            if (data[position] != TsPacket.SyncByte)
            {
                position++;
                continue;
            }

            var packet = new TsPacket(data.Slice(position, TsPacket.Size));

            if (packet.Pid == PatPid && packet.PayloadUnitStart && IsNewGop(data, position, lastOffset))
            {
                offsets.Add(position);
                lastOffset = position;

                if (offsets.Count >= maxCount)
                    return offsets;
            }

            position += TsPacket.Size;
        }

        if (offsets.Count == 0)
            offsets.Add(0);

        return offsets;
    }

    // ------------------------------------------------------------------
    // Keyframe detection
    // ------------------------------------------------------------------

    private static bool IsNewGop(ReadOnlySpan<byte> data, int patOffset, int lastGopOffset)
    {
        if (lastGopOffset < 0)
            return true;

        return patOffset - lastGopOffset >= MinGopSizeBytes && HasKeyframeAhead(data, patOffset);
    }

    /// <summary>Looks at the packets following a PAT for a keyframe marker.</summary>
    private static bool HasKeyframeAhead(ReadOnlySpan<byte> data, int patOffset)
    {
        for (var i = 1; i <= KeyframeLookaheadPackets; i++)
        {
            var offset = patOffset + i * TsPacket.Size;
            if (offset + TsPacket.Size > data.Length)
                return false;

            var packet = new TsPacket(data.Slice(offset, TsPacket.Size));
            if (!packet.HasSyncByte)
                continue;

            if (packet.IsRandomAccess)
                return true;

            if (packet.PayloadUnitStart && ContainsKeyframeNal(packet.Payload))
                return true;
        }

        return false;
    }

    /// <summary>True if the payload holds an H.264 IDR slice or SPS NAL unit.</summary>
    private static bool ContainsKeyframeNal(ReadOnlySpan<byte> payload)
    {
        var rest = payload;

        while (true)
        {
            // Matches both 00 00 01 and 00 00 00 01 (the latter contains the former).
            var start = rest.IndexOf(StartCodePrefix);
            if (start < 0)
                return false;

            var nalHeaderIndex = start + StartCodePrefix.Length;
            if (nalHeaderIndex >= rest.Length)
                return false;

            var nalType = rest[nalHeaderIndex] & NalTypeMask;
            if (nalType is IdrSliceNalType or SpsNalType)
                return true;

            rest = rest[(start + 1)..];
        }
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private byte[] Buffer =>
        _buffer ?? throw new InvalidOperationException("TsSegment has not been loaded.");

    private ReadOnlySpan<byte> Data => Buffer.AsSpan(0, _length);

    /// <summary>A read-only view over one 188-byte transport stream packet.</summary>
    private readonly ref struct TsPacket
    {
        public const int Size = 188;
        public const byte SyncByte = 0x47;

        private const int PayloadUnitStartMask = 0x40;
        private const int RandomAccessMask = 0x40;

        private readonly ReadOnlySpan<byte> _bytes;

        public TsPacket(ReadOnlySpan<byte> bytes) => _bytes = bytes;

        public bool HasSyncByte => _bytes[0] == SyncByte;

        public bool PayloadUnitStart => (_bytes[1] & PayloadUnitStartMask) != 0;

        public int Pid => ((_bytes[1] & 0x1F) << 8) | _bytes[2];

        /// <summary>The adaptation field's random access flag (a keyframe hint from the muxer).</summary>
        public bool IsRandomAccess =>
            HasAdaptationField && _bytes[4] > 0 && (_bytes[5] & RandomAccessMask) != 0;

        /// <summary>The packet payload, or empty if there is none.</summary>
        public ReadOnlySpan<byte> Payload
        {
            get
            {
                if (!HasPayload)
                    return default;

                var start = HasAdaptationField ? 5 + _bytes[4] : 4;
                return start < Size ? _bytes[start..] : default;
            }
        }

        // 01 = payload only, 10 = adaptation field only, 11 = both.
        private int AdaptationFieldControl => (_bytes[3] >> 4) & 0x03;
        private bool HasAdaptationField => AdaptationFieldControl is 0b10 or 0b11;
        private bool HasPayload => AdaptationFieldControl is 0b01 or 0b11;
    }
}