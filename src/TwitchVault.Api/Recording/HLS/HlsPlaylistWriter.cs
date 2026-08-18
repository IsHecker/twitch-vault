using System.Text;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

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

    public long LastTwitchMediaSequence { get; private set; }
    public string? LastSegmentFileName { get; private set; }
    public bool HasInitSegment { get; private set; }

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stream _fileStream;
    private readonly DateTime _startTime;
    private readonly bool _isFinalized;
    private float _targetDuration;
    private float _totalDuration;
    private bool _lastEntryWasDiscontinuity;

    private HlsPlaylistWriter(Stream fileStream, PlaylistState state)
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

            await WriteHeaderAsync(cancellationToken);
            await WriteLineAsync(HlsTags.ExtInf(duration), cancellationToken);
            await WriteLineAsync(fileName, cancellationToken);
            await _fileStream.FlushAsync(cancellationToken);

            LastSegmentFileName = fileName;
            _lastEntryWasDiscontinuity = false;
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
        {
            _fileStream.SetLength(_fileStream.Length - endListBytes);
        }
    }

    private async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        await WriteLineAsync(HlsTags.EndList, cancellationToken);
        await _fileStream.FlushAsync(cancellationToken);
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

    private ValueTask WriteAsync(string text, CancellationToken ct = default)
        => _fileStream.WriteAsync(Encoding.UTF8.GetBytes(text), ct);

    private ValueTask WriteLineAsync(string text, CancellationToken ct = default)
        => WriteAsync(text + '\n', ct);

    public async ValueTask DisposeAsync()
    {
        await FinalizeAsync();
        await _fileStream.DisposeAsync();
        _lock.Dispose();
    }
}