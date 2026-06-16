using System.Globalization;
using System.Text;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public sealed class HlsPlaylist : IAsyncDisposable
{
    public long LastMediaSequence { get; private set; } = -1;
    public string? LastSegmentFileName { get; private set; }
    public bool HasInitSegment => !string.IsNullOrWhiteSpace(_initSegmentFileName);

    private readonly string _playlistPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly FileStream _fileStream;
    private readonly StreamWriter _streamWriter;
    private readonly PeriodicTimer _flushTimer;
    private readonly List<PlaylistEntry> _entries = [];
    private readonly DateTime _startTime;
    private string? _initSegmentFileName;
    private float _targetDuration;
    private float _totalDuration;
    private bool _isFinalized;
    private bool _isDisposed;

    private HlsPlaylist(
        string playlistPath,
        DateTime startTime,
        int flushIntervalInSec)
    {
        _playlistPath = playlistPath;
        _startTime = startTime;
        _flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(flushIntervalInSec));

        _fileStream = new FileStream(
            _playlistPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        _streamWriter = new StreamWriter(_fileStream, Encoding.UTF8);
    }

    public static async Task<HlsPlaylist> LoadOrCreateAsync(
        string streamFolderPath,
        IDateTimeProvider dateTimeProvider,
        int flushIntervalInSec)
    {
        var playlistPath = Path.Combine(streamFolderPath, "playlist.m3u8");
        if (!File.Exists(playlistPath))
            return new(playlistPath, dateTimeProvider.DateTimeNow, flushIntervalInSec);

        var lines = await File.ReadAllLinesAsync(playlistPath);
        var playlist = new HlsPlaylist(playlistPath, ParseStartTime(lines, dateTimeProvider), flushIntervalInSec);
        playlist.RestoreFrom(lines);

        return playlist;
    }

    public void AddInitSegment(string fileName) => _initSegmentFileName = fileName;

    public void UpdateMediaSequence(long mediaSequence) => LastMediaSequence = mediaSequence;

    public void AddSegment(string fileName, float duration)
    {
        _lock.Wait();
        try
        {
            _entries.Add(new PlaylistEntry(fileName, duration));
            _totalDuration += duration;

            if (duration > _targetDuration)
                _targetDuration = duration;

            LastSegmentFileName = fileName;
        }
        finally { _lock.Release(); }
    }

    public void AddDiscontinuity()
    {
        _lock.Wait();
        try
        {
            if (_entries.Count == 0 || _entries[^1].IsDiscontinuity)
                return;

            _entries.Add(PlaylistEntry.Discontinuity);
        }
        finally { _lock.Release(); }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        _isFinalized = true;
        await FlushAsync(cancellationToken);
    }

    private void RestoreFrom(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("#TWITCH-MEDIA-SEQUENCE"))
            {
                UpdateMediaSequence(long.Parse(HlsTagReader.ReadTagValue(line, "#TWITCH-MEDIA-SEQUENCE")));
                continue;
            }

            if (line.StartsWith("#EXT-X-MAP:URI"))
            {
                _initSegmentFileName = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
                continue;
            }

            if (line == "#EXT-X-DISCONTINUITY")
            {
                _entries.Add(PlaylistEntry.Discontinuity);
                continue;
            }

            if (line.StartsWith("#EXTINF"))
            {
                var durationStr = HlsTagReader.ReadTagValue(line, "#EXTINF", ',');
                if (!float.TryParse(durationStr, out var duration) || i + 1 >= lines.Length)
                    continue;

                var fileName = lines[++i];
                AddSegment(fileName, duration);
            }
        }

        AddDiscontinuity();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            string content = Build();

            // _fileStream.Seek(0, SeekOrigin.Begin);
            // await _streamWriter.WriteAsync(content);
            // _fileStream.SetLength(_fileStream.Position);


            _fileStream.Position = 0;
            _fileStream.SetLength(0);

            await _streamWriter.WriteAsync(content);
            await _streamWriter.FlushAsync(cancellationToken); // important
        }
        finally
        {
            _lock.Release();
        }
    }

    private string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(_targetDuration)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"#ID3-EQUIV-TDTG:{_startTime:yyyy-MM-ddTHH:mm:ss}");
        sb.AppendLine("#EXT-X-PLAYLIST-TYPE:EVENT");
        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:1");
        sb.AppendLine($"#TWITCH-MEDIA-SEQUENCE:{LastMediaSequence}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"#EXT-X-TOTAL-SECS:{_totalDuration:F3}");

        if (_initSegmentFileName is not null)
            sb.AppendLine($"#EXT-X-MAP:URI=\"{_initSegmentFileName}\"");

        foreach (var entry in _entries)
        {
            if (entry.IsDiscontinuity)
            {
                sb.AppendLine("#EXT-X-DISCONTINUITY");
                continue;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"#EXTINF:{entry.Duration:F3},");
            sb.AppendLine(entry.FileName);
        }

        if (_isFinalized)
            sb.AppendLine("#EXT-X-ENDLIST");

        return sb.ToString();
    }

    private static DateTime ParseStartTime(string[] lines, IDateTimeProvider dateTimeProvider)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("#ID3-EQUIV-TDTG:") &&
                DateTime.TryParse(line[16..], CultureInfo.InvariantCulture, out var time))
                return time;
        }

        return dateTimeProvider.DateTimeNow;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _flushTimer.Dispose();

        await FlushAsync();
        await _fileStream.DisposeAsync();
        _lock.Dispose();
    }

    private record struct PlaylistEntry(string FileName, float Duration, bool IsDiscontinuity = false)
    {
        public float Duration { get; set; } = Duration;
        public static readonly PlaylistEntry Discontinuity = new("#EXT-X-DISCONTINUITY", 0f);
    }
}