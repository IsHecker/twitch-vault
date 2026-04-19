using System.Globalization;
using System.Text;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Services;

public sealed class PlaylistBuilder : IAsyncDisposable
{
    private readonly string _playlistPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly PeriodicTimer _flushTimer;
    private readonly CancellationToken _cancellationToken;

    private readonly List<PlaylistEntry> _entries = [];
    private readonly DateTime _startTime;

    private long _twitchMediaSequence = -1;
    private string? _initSegmentFileName;
    private float _targetDuration;
    private float _totalDuration;
    private bool _isEnded;

    public string? LastSegmentFileName { get; private set; }
    public bool IsInitSegmentSet => !string.IsNullOrWhiteSpace(_initSegmentFileName);
    public long TwitchMediaSequence => _twitchMediaSequence;

    private PlaylistBuilder(string playlistPath, DateTime startTime, int flushIntervalInSec, CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _playlistPath = playlistPath;
        _startTime = startTime;
        _flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(flushIntervalInSec));

        _ = FlushLoopAsync();
    }

    public static async Task<PlaylistBuilder> LoadOrCreateAsync(
        string streamFolderPath,
        int flushIntervalInSec,
        CancellationToken cancellationToken)
    {
        var playlistPath = Path.Combine(streamFolderPath, "playlist.m3u8");

        if (!File.Exists(playlistPath))
            return new(playlistPath, DateTime.UtcNow, flushIntervalInSec, cancellationToken);

        var lines = await File.ReadAllLinesAsync(playlistPath, cancellationToken);
        var builder = new PlaylistBuilder(playlistPath, ParseStartTime(lines), flushIntervalInSec, cancellationToken);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("#TWITCH-MEDIA-SEQUENCE"))
            {
                builder.SetTwitchMediaSequence(long.Parse(HlsTagReader.ReadTagValue(line, "#TWITCH-MEDIA-SEQUENCE")));
            }
            else if (line.StartsWith("#EXT-X-MAP:URI"))
            {
                builder._initSegmentFileName = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
            }
            else if (line == "#EXT-X-DISCONTINUITY")
            {
                builder._entries.Add(PlaylistEntry.Discontinuity);
            }
            else if (line.StartsWith("#EXTINF"))
            {
                var durationStr = HlsTagReader.ReadTagValue(line, "#EXTINF", ',');

                if (!float.TryParse(durationStr, out var duration) || i + 1 >= lines.Length)
                    continue;

                var fileName = lines[++i];
                builder.AddSegment(fileName, duration);
            }
        }

        builder.AddDiscontinuity();
        // builder._isEnded = lines.Any(l => l == "#EXT-X-ENDLIST");

        return builder;
    }

    public void AddMap(string fileName)
    {
        _lock.Wait();
        try { _initSegmentFileName = fileName; }
        finally { _lock.Release(); }
    }

    public void SetTwitchMediaSequence(long mediaSequence)
    {
        _lock.Wait();
        try { _twitchMediaSequence = mediaSequence; }
        finally { _lock.Release(); }
    }

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

    public async Task FinalizeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _isEnded = true; }
        finally { _lock.Release(); }

        await FlushAsync(ct);
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (await _flushTimer.WaitForNextTickAsync(_cancellationToken))
                await FlushAsync(_cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task FlushAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            string content = Build();

            await using var stream = new FileStream(_playlistPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(content);
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
        sb.AppendLine($"#TWITCH-MEDIA-SEQUENCE:{_twitchMediaSequence}");
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

        if (_isEnded)
            sb.AppendLine("#EXT-X-ENDLIST");

        return sb.ToString();
    }

    private static DateTime ParseStartTime(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("#ID3-EQUIV-TDTG:") &&
                DateTime.TryParse(line[16..], CultureInfo.InvariantCulture, out var time))
                return time;
        }
        return DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();

        _flushTimer.Dispose();
        _lock.Dispose();
    }

    public record PlaylistEntry(string FileName, float Duration)
    {
        public float Duration { get; set; } = Duration;

        public bool IsDiscontinuity => FileName == "#EXT-X-DISCONTINUITY";
        public static readonly PlaylistEntry Discontinuity = new("#EXT-X-DISCONTINUITY", 0f);
    }
}