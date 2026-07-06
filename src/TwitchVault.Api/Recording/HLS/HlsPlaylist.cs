using System.Text;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public interface IHlsPlaylist : IAsyncDisposable
{
    long LastTwitchMediaSequence { get; }
    string? LastSegmentFileName { get; }
    bool HasInitSegment { get; }

    Task SetInitSegmentAsync(string fileName, CancellationToken cancellationToken = default);
    Task AddSegmentAsync(string fileName, float duration, CancellationToken cancellationToken = default);
    Task AddDiscontinuityAsync(CancellationToken cancellationToken = default);
    Task FinalizeAsync(CancellationToken cancellationToken = default);
    void UpdateTwitchMediaSequence(long mediaSequence);
}

public sealed class HlsPlaylist : IHlsPlaylist
{
    private const string PlaylistFileName = "playlist.m3u8";
    private const int TargetDurationDigits = 3;
    private const int MediaSequenceDigits = 12;
    private const string TotalSecondsFormat = "000000000000.000";

    public long LastTwitchMediaSequence { get; private set; }
    public string? LastSegmentFileName { get; private set; }
    public bool HasInitSegment { get; private set; }

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stream _fileStream;
    private float _targetDuration;
    private bool _lastEntryWasDiscontinuity;
    private int _flushedEntryCount = 0;


    private readonly DateTime _startTime;
    private float _totalDuration;
    private bool _isFinalized;

    private HlsPlaylist(Stream fileStream, PlaylistState state)
    {
        _fileStream = fileStream;
        _startTime = state.StartTime;

        LastTwitchMediaSequence = state.SegmentCount;
        LastSegmentFileName = state.LastSegmentFileName;
        HasInitSegment = state.HasInitSegment;
        _isFinalized = state.IsFinalized;
    }

    public static async Task<HlsPlaylist> LoadOrCreateAsync(
        string streamFolderPath,
        IDateTimeProvider dateTimeProvider,
        IFileSystem fileSystem,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(streamFolderPath, PlaylistFileName);
        var exists = fileSystem.Exists(path);

        var state = exists
            ? PlaylistStateRestorer.Restore(await fileSystem.ReadAllLinesAsync(path, cancellationToken))
            : PlaylistState.Empty(dateTimeProvider.DateTimeNow);

        var fileStream = fileSystem.OpenWrite(path, FileMode.OpenOrCreate);

        var playlist = new HlsPlaylist(fileStream, state);

        if (!exists)
            await playlist.WriteHeaderAsync(cancellationToken);
        else if (!state.IsFinalized)
            await playlist.AddDiscontinuityAsync(cancellationToken);

        return playlist;
    }

    public async Task SetInitSegmentAsync(string fileName, CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (fileName is null || duration <= 0)
                return;

            _totalDuration += duration;
            if (duration > _targetDuration)
                _targetDuration = duration;

            await WriteHeaderAsync(cancellationToken);
            await WriteLineAsync(HlsTags.ExtInf(duration), cancellationToken);
            await WriteLineAsync(fileName, cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);

            _flushedEntryCount++;
            LastSegmentFileName = fileName;
            _lastEntryWasDiscontinuity = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddDiscontinuityAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_flushedEntryCount < 1 || _lastEntryWasDiscontinuity)
                return;

            await WriteHeaderAsync(cancellationToken);
            await WriteLineAsync(HlsTags.Discontinuity, cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);
            _lastEntryWasDiscontinuity = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_isFinalized)
                return;

            await WriteLineAsync(HlsTags.EndList, cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);
            _isFinalized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void UpdateTwitchMediaSequence(long mediaSequence) => LastTwitchMediaSequence = mediaSequence;

    public async ValueTask DisposeAsync()
    {
        // await FlushAsync();
        await _fileStream.FlushAsync();
        await _fileStream.DisposeAsync();
        _lock.Dispose();
    }

    // private async Task WriteHeaderAsync(CancellationToken cancellationToken)
    // {
    //     await _writer.FlushAsync(cancellationToken);

    //     var lastPosition = _fileStream.Position;
    //     _fileStream.Position = 0;

    //     await _writer.WriteLineAsync(HlsTags.ExtM3U);
    //     await _writer.WriteLineAsync(HlsTags.Version(6));
    //     await _writer.WriteLineAsync(HlsTags.TargetDuration((int)Math.Ceiling(_targetDuration)));
    //     await _writer.WriteLineAsync(HlsTags.StartTime(_startTime));
    //     await _writer.WriteLineAsync(HlsTags.PlaylistTypeEvent);
    //     await _writer.WriteLineAsync(HlsTags.MediaSequence(1));
    //     await _writer.WriteLineAsync(HlsTags.TwitchMediaSequence(LastTwitchMediaSequence));
    //     await _writer.WriteLineAsync(HlsTags.TotalSeconds(_totalDuration));

    //     await _writer.FlushAsync(cancellationToken);

    //     _fileStream.Position = lastPosition;
    // }

    private async Task WriteHeaderAsync(CancellationToken cancellationToken = default)
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
            // .Append(HlsTags.TargetDuration((int)Math.Ceiling(_targetDuration))).AppendLine()
            .Append($"{HlsTags.TargetDurationPrefix}:{targetDurationValue}").AppendLine()
            .Append(HlsTags.StartTime(_startTime)).AppendLine()
            .Append(HlsTags.PlaylistTypeEvent).AppendLine()
            .Append(HlsTags.MediaSequence(1)).AppendLine()
            .Append($"{HlsTags.TwitchMediaSequencePrefix}:{mediaSequenceValue}").AppendLine()
            .Append($"{HlsTags.TotalSecondsPrefix}:{totalSecondsValue}").AppendLine()
            .ToString();

        var endPosition = _fileStream.Length;

        _fileStream.Position = 0;

        await WriteAsync(header, cancellationToken);
        await _fileStream.FlushAsync(cancellationToken);

        _fileStream.Position = endPosition;
    }

    private ValueTask WriteAsync(string text, CancellationToken ct = default)
        => _fileStream.WriteAsync(Encoding.UTF8.GetBytes(text), ct);

    private ValueTask WriteLineAsync(string text, CancellationToken ct = default)
        => WriteAsync(text + '\n', ct);
}