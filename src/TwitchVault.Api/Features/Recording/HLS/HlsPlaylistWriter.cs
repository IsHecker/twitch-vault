using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TwitchVault.Api.Features.Recording.HLS;

public interface IHlsPlaylistWriter : IAsyncDisposable
{
    long LastTwitchMediaSequence { get; }
    string? LastSegmentFileName { get; }
    bool HasInitSegment { get; }

    Task SetInitSegmentAsync(string fileName, CancellationToken cancellationToken);
    Task AddSegmentAsync(string fileName, float duration, CancellationToken cancellationToken);
    Task AddDiscontinuityAsync(CancellationToken cancellationToken);
    void UpdateTwitchMediaSequence(long mediaSequence);
}

public sealed class HlsPlaylistWriter : IHlsPlaylistWriter
{
    private const string PlaylistFileName = "playlist.m3u8";
    private const int TargetDurationDigits = 3;
    private const int MediaSequenceDigits = 12;
    private const string TotalSecondsFormat = "000000000000.000";
    private static readonly TimeSpan MinFlushInterval = TimeSpan.FromSeconds(5);

    private const int LineBufferSize = 256;

    public long LastTwitchMediaSequence { get; private set; }
    public string? LastSegmentFileName { get; private set; }
    public bool HasInitSegment { get; private set; }

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly System.IO.Stream _fileStream;
    private readonly DateTime _startTime;
    private readonly bool _isFinalized;
    private readonly byte[] _lineBuffer = new byte[LineBufferSize];
    private readonly Stopwatch _flushStopwatch = Stopwatch.StartNew();
    private float _targetDuration;
    private float _totalDuration;
    private bool _lastEntryWasDiscontinuity;

    private HlsPlaylistWriter(System.IO.Stream fileStream, PlaylistState state)
    {
        _fileStream = fileStream;
        _startTime = state.StartTime;
        _targetDuration = state.TargetDuration;
        _totalDuration = state.TotalDuration;
        _lastEntryWasDiscontinuity = state.LastEntryWasDiscontinuity;
        _isFinalized = state.IsFinalized;

        LastTwitchMediaSequence = state.SegmentCount;
        LastSegmentFileName = state.LastSegmentFileName;
        HasInitSegment = state.HasInitSegment;
    }

    public static async Task<HlsPlaylistWriter> LoadOrCreateAsync(
        string streamFolderPath,
        IDateTimeProvider dateTimeProvider,
        IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(streamFolderPath, PlaylistFileName);
        var exists = fileSystem.Exists(path);

        var state = exists
            ? PlaylistStateRestorer.Restore(await fileSystem.ReadAllLinesAsync(path, cancellationToken))
            : PlaylistState.Empty(dateTimeProvider.DateTimeNow);

        var fileStream = fileSystem.OpenWrite(path, FileMode.OpenOrCreate);

        var playlist = new HlsPlaylistWriter(fileStream, state);

        if (!exists)
            await playlist.WriteHeaderAsync(cancellationToken);
        else
            await playlist.ResumeAsync(cancellationToken);

        return playlist;
    }

    public async Task SetInitSegmentAsync(string fileName, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (HasInitSegment)
                throw new InvalidOperationException("An init segment has already been set for this playlist.");

            await WriteHeaderAsync(cancellationToken);
            await WriteLineAsync(HlsTags.Map(fileName), cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);
            HasInitSegment = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddSegmentAsync(
        string fileName,
        float duration,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (fileName is null || duration <= 0)
                return;

            _totalDuration += duration;
            if (duration > _targetDuration)
                _targetDuration = duration;

            await WriteExtInfAsync(duration, cancellationToken);
            await WriteLineAsync(fileName, cancellationToken);

            LastSegmentFileName = fileName;
            _lastEntryWasDiscontinuity = false;

            await FlushIfDueAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddDiscontinuityAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(LastSegmentFileName) || _lastEntryWasDiscontinuity)
                return;

            _fileStream.Position = _fileStream.Length;

            await WriteLineAsync(HlsTags.Discontinuity, cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);
            _lastEntryWasDiscontinuity = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void UpdateTwitchMediaSequence(long mediaSequence) => LastTwitchMediaSequence = mediaSequence;

    private async Task FlushIfDueAsync(CancellationToken cancellationToken)
    {
        if (_flushStopwatch.Elapsed < MinFlushInterval)
            return;

        await _fileStream.FlushAsync(cancellationToken);
        _flushStopwatch.Restart();
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (_isFinalized)
            RemoveEndListTag();

        await AddDiscontinuityAsync(cancellationToken);
    }

    private void RemoveEndListTag()
    {
        var endListBytes = Encoding.UTF8.GetByteCount($"{HlsTags.EndList}\n");
        if (_fileStream.Length >= endListBytes)
            _fileStream.SetLength(_fileStream.Length - endListBytes);
    }

    private async Task WriteHeaderAsync(CancellationToken cancellationToken)
    {
        var targetDurationValue = ((int)Math.Ceiling(_targetDuration))
            .ToString()
            .PadLeft(TargetDurationDigits, '0');

        var mediaSequenceValue = LastTwitchMediaSequence
            .ToString()
            .PadLeft(MediaSequenceDigits, '0');

        var totalSecondsValue = _totalDuration.ToString(TotalSecondsFormat);

        var header = new StringBuilder()
            .Append(HlsTags.ExtM3U).AppendLine()
            .Append(HlsTags.Version(6)).AppendLine()
            .Append(HlsTags.TargetDuration(targetDurationValue)).AppendLine()
            .Append(HlsTags.StartTime(_startTime)).AppendLine()
            .Append(HlsTags.PlaylistTypeEvent).AppendLine()
            .Append(HlsTags.MediaSequence(1)).AppendLine()
            .Append(HlsTags.TwitchMediaSequence(mediaSequenceValue)).AppendLine()
            .Append(HlsTags.TotalSeconds(totalSecondsValue)).AppendLine()
            .ToString();

        _fileStream.Position = 0;

        await WriteAsync(header, cancellationToken);
        await _fileStream.FlushAsync(cancellationToken);

        _fileStream.Position = _fileStream.Length;
    }

    private async ValueTask WriteExtInfAsync(float duration, CancellationToken ct)
    {
        var buffer = _lineBuffer;
        var bytesWritten = 0;

        "#EXTINF:"u8.CopyTo(buffer.AsSpan(bytesWritten));
        bytesWritten += 8;

        if (duration.TryFormat(buffer.AsSpan(bytesWritten), out var written, "F3", CultureInfo.InvariantCulture))
        {
            bytesWritten += written;
        }

        buffer[bytesWritten++] = (byte)',';
        buffer[bytesWritten++] = (byte)'\n';

        await _fileStream.WriteAsync(buffer.AsMemory(0, bytesWritten), ct);
    }

    private ValueTask WriteAsync(string text, CancellationToken ct)
        => WriteCoreAsync(text, appendNewline: false, ct);

    private ValueTask WriteLineAsync(string text, CancellationToken ct)
        => WriteCoreAsync(text, appendNewline: true, ct);

    private async ValueTask WriteCoreAsync(string text, bool appendNewline, CancellationToken ct)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length) + (appendNewline ? 1 : 0);

        var rented = maxByteCount > _lineBuffer.Length
            ? ArrayPool<byte>.Shared.Rent(maxByteCount) : null;
        var buffer = rented ?? _lineBuffer;

        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(text, buffer);
            if (appendNewline)
                buffer[bytesWritten++] = (byte)'\n';

            await _fileStream.WriteAsync(buffer.AsMemory(0, bytesWritten), ct);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await WriteHeaderAsync(CancellationToken.None);
        await WriteLineAsync(HlsTags.EndList, CancellationToken.None);
        await _fileStream.FlushAsync();
        await _fileStream.DisposeAsync();
        _lock.Dispose();
    }
}