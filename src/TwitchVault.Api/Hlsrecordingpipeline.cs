//using System.Globalization;
//using System.Runtime.CompilerServices;
//using TwitchVault.Api.Common;
//using TwitchVault.Api.Persistence;
//using TwitchVault.Api.Recording.HLS;
//using TwitchVault.Api.Twitch;

//namespace TwitchVault.Api.Anything;

//// =============================================================================
//// Domain / value types
//// =============================================================================

//public readonly record struct MediaPlaylist(int Bandwidth, string Url);

//public readonly record struct HlsSegment(string Url, float DurationSeconds);

///// <summary>A completed local file ready to be registered in the playlist.</summary>
//public record CompletedSegmentFile(string FileName, float TotalDuration);

///// <summary>Everything a single manifest poll produces.</summary>
//public record ParsedManifest(
//    IReadOnlyList<HlsSegment> NewSegments,
//    string? InitSegmentUrl,        // null when not present or already handled
//    long LastMediaSequence,
//    bool IsStreamEnded);

//// =============================================================================
//// Stage 1 — ManifestSource
//// Owns: variant resolution, quality selection, manifest fetching.
//// Produces: raw manifest strings on demand.
//// =============================================================================

//public sealed class ManifestSource
//{
//    private static readonly TimeSpan VariantRefreshInterval = TimeSpan.FromSeconds(30);

//    private const int MinPollsBeforeStabilization = 5;
//    private const int StableCountThreshold = 10;
//    private const int VariantCountForEarlyStabilization = 5;

//    private readonly string _channelName;
//    private readonly TwitchGqlClient _twitchClient;
//    private readonly ChannelRepository _channelRepository;
//    private readonly ILogger _logger;

//    private MediaPlaylist[] _variants = [];
//    private int _pollCount;
//    private int _stableCount;
//    private bool _stabilized;
//    private DateTimeOffset _lastVariantFetchAt = DateTimeOffset.MinValue;

//    // Tracks the active variant so we can detect quality switches.
//    private string? _activeVariantUrl;
//    private int _activeQualityRank = -1;

//    public ManifestSource(
//        string channelName,
//        TwitchGqlClient twitchClient,
//        ChannelRepository channelRepository,
//        ILogger logger)
//    {
//        _channelName = channelName;
//        _twitchClient = twitchClient;
//        _channelRepository = channelRepository;
//        _logger = logger;
//    }

//    /// <summary>
//    /// Fetches the next media manifest for the active quality variant.
//    /// Returns null when Twitch reports no variants yet (stream starting up).
//    /// </summary>
//    public async Task<(string? Manifest, bool QualityChanged)> GetNextManifestAsync(CancellationToken ct)
//    {
//        await RefreshVariantsIfNeededAsync(ct);

//        if (_variants.Length == 0)
//            return (null, false);

//        var (rank, url) = await ResolveQualityAsync(ct);

//        var qualityChanged = rank != _activeQualityRank || url != _activeVariantUrl;
//        (_activeQualityRank, _activeVariantUrl) = (rank, url);

//        var manifest = await _twitchClient.GetPlaylistContentAsync(url, ct);
//        return (string.IsNullOrWhiteSpace(manifest) ? null : manifest, qualityChanged);
//    }

//    private async Task RefreshVariantsIfNeededAsync(CancellationToken ct)
//    {
//        if (_stabilized)
//            return;

//        if (DateTimeOffset.UtcNow - _lastVariantFetchAt < VariantRefreshInterval)
//            return;

//        _lastVariantFetchAt = DateTimeOffset.UtcNow;

//        var master = await _twitchClient.GetMasterPlaylistAsync(_channelName, ct);
//        if (string.IsNullOrWhiteSpace(master))
//            return;

//        var fetched = MasterPlaylistParser.ParseVariants(master);
//        if (fetched.Length == 0)
//            return;

//        _stableCount = fetched.Length > _variants.Length ? 0 : _stableCount + 1;
//        _variants = fetched;
//        _pollCount++;

//        if (!IsStabilized())
//            return;

//        _stabilized = true;
//        _logger.LogInformation(
//            "Variants stabilized: {Count} qualities, top bandwidth {Bandwidth} bps.",
//            _variants.Length, _variants[^1].Bandwidth);
//    }

//    private async Task<(int Rank, string Url)> ResolveQualityAsync(CancellationToken ct)
//    {
//        var channels = await _channelRepository.GetAllAsync();
//        var requestedRank = channels.First(c => c.Name == _channelName).QualityRank - 1;
//        var clampedRank = Math.Clamp(requestedRank, 0, _variants.Length - 1);

//        if (requestedRank != clampedRank)
//            _logger.LogInformation(
//                "Quality rank {Requested} unavailable; clamped to {Clamped} of {Total}.",
//                requestedRank + 1, clampedRank + 1, _variants.Length);

//        return (clampedRank, _variants[clampedRank].Url);
//    }

//    private bool IsStabilized() =>
//        _pollCount >= MinPollsBeforeStabilization &&
//        (_variants.Length >= VariantCountForEarlyStabilization || _stableCount >= StableCountThreshold);
//}

//// =============================================================================
//// Stage 2 — SegmentFilter  (pure / stateless)
//// Owns: manifest parsing, sequence-based deduplication.
//// Produces: ParsedManifest — only segments not yet seen.
//// =============================================================================

//public static class SegmentFilter
//{

//    public static ParsedManifest Parse(string manifestContent, long lastKnownSequence)
//    {
//        var sequenceStr = HlsTagReader.ReadTagValue(manifestContent, "#EXT-X-MEDIA-SEQUENCE");
//        if (!long.TryParse(sequenceStr, out var firstSequence))
//            return new ParsedManifest([], null, lastKnownSequence, IsStreamEnded: false);

//        var lines = manifestContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
//        var segments = new List<HlsSegment>();
//        string? initSegmentUrl = null;
//        long currentSequence = firstSequence - 1;

//        for (int i = 0; i < lines.Length; i++)
//        {
//            var line = lines[i].TrimEnd('\r');

//            if (line.StartsWith("#EXT-X-MAP:URI", StringComparison.Ordinal))
//            {
//                initSegmentUrl = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
//                continue;
//            }

//            if (!line.StartsWith("#EXTINF", StringComparison.Ordinal))
//                continue;

//            currentSequence++;

//            if (currentSequence <= lastKnownSequence)
//            {
//                i++; // skip the URL line belonging to this already-seen segment
//                continue;
//            }

//            var durationStr = HlsTagReader.ReadTagValue(line, "#EXTINF", ',');
//            if (!float.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
//                continue;

//            if (i + 1 >= lines.Length)
//                break;

//            segments.Add(new HlsSegment(lines[++i].TrimEnd('\r'), duration));
//        }

//        var ended = manifestContent.Contains("#EXT-X-ENDLIST", StringComparison.Ordinal);
//        return new ParsedManifest(segments, initSegmentUrl, currentSequence, ended);
//    }

//}

//// =============================================================================
//// Stage 3 — SegmentAccumulator
//// Owns: downloading segments, concatenating them into local files,
////       deciding when a file is "full" based on duration.
//// Produces: CompletedSegmentFile — handed back to the coordinator.
//// Knows nothing about PlaylistBuilder.
//// =============================================================================

//public sealed class SegmentAccumulator : IAsyncDisposable
//{
//    private const string SegmentPrefix = "seg_";
//    private const string InitFileName = "init";
//    private const int FileBufferSize = 8192;

//    private readonly TwitchGqlClient _twitchClient;
//    private readonly string _streamFolderPath;
//    private readonly int _maxSegmentDurationSeconds;

//    private int _nextSegmentIndex;
//    private FileStream? _currentFile;
//    private string? _currentFileName;
//    private float _currentDuration;

//    public bool InitSegmentDownloaded { get; private set; }

//    public SegmentAccumulator(
//        TwitchGqlClient twitchClient,
//        string streamFolderPath,
//        int maxSegmentDurationSeconds,
//        int resumeFromSegmentIndex = 1)
//    {
//        _twitchClient = twitchClient;
//        _streamFolderPath = streamFolderPath;
//        _maxSegmentDurationSeconds = maxSegmentDurationSeconds;
//        _nextSegmentIndex = resumeFromSegmentIndex;
//    }

//    /// <summary>
//    /// Downloads all segments in <paramref name="manifest"/> and accumulates them into local files.
//    /// Returns one <see cref="CompletedSegmentFile"/> for every file that was sealed during this call.
//    /// </summary>
//    public async IAsyncEnumerable<CompletedSegmentFile> AccumulateAsync(
//        ParsedManifest manifest,
//        [EnumeratorCancellation] CancellationToken ct)
//    {
//        if (manifest.InitSegmentUrl is { } initUrl && !InitSegmentDownloaded)
//        {
//            await DownloadInitSegmentAsync(initUrl, ct);
//            InitSegmentDownloaded = true;
//        }

//        foreach (var segment in manifest.NewSegments)
//        {
//            _currentFile ??= OpenNextFile(segment.Url);

//            await using var data = await _twitchClient.DownloadAsStreamAsync(segment.Url, ct);
//            await data.CopyToAsync(_currentFile, ct);
//            _currentDuration += segment.DurationSeconds;

//            if (_currentDuration >= _maxSegmentDurationSeconds)
//            {
//                yield return await SealCurrentFileAsync();
//            }
//        }
//    }

//    /// <summary>
//    /// Seals whatever partial file is in progress and returns it.
//    /// Call this on quality switch or stream end.
//    /// </summary>
//    public async Task<CompletedSegmentFile?> FlushAsync()
//    {
//        if (_currentFile is null || _currentDuration <= 0)
//            return null;

//        return await SealCurrentFileAsync();
//    }

//    private async Task<CompletedSegmentFile> SealCurrentFileAsync()
//    {
//        await _currentFile!.DisposeAsync();
//        _currentFile = null;

//        var completed = new CompletedSegmentFile(_currentFileName!, _currentDuration);
//        _currentFileName = null;
//        _currentDuration = 0;
//        return completed;
//    }

//    private FileStream OpenNextFile(string segmentUrl)
//    {
//        var extension = GetUrlExtension(segmentUrl);
//        _currentFileName = $"{SegmentPrefix}{_nextSegmentIndex++}{extension}";
//        var path = Path.Combine(_streamFolderPath, _currentFileName);
//        return OpenFile(path, FileMode.Create);
//    }

//    private async Task DownloadInitSegmentAsync(string url, CancellationToken ct)
//    {
//        var fileName = $"{InitFileName}{GetUrlExtension(url)}";
//        var path = Path.Combine(_streamFolderPath, fileName);

//        await using var data = await _twitchClient.DownloadAsStreamAsync(url, ct);
//        await using var file = OpenFile(path, FileMode.Create);
//        await data.CopyToAsync(file, ct);
//    }

//    public async ValueTask DisposeAsync()
//    {
//        if (_currentFile is not null)
//            await _currentFile.DisposeAsync();
//    }

//    private static FileStream OpenFile(string path, FileMode mode) =>
//        new(path, mode, FileAccess.Write, FileShare.Read, FileBufferSize, useAsync: true);

//    private static string GetUrlExtension(string url) =>
//        Path.GetExtension(new Uri(url).AbsolutePath);
//}

//// =============================================================================
//// Stage 4 — PlaylistWriter  (thin wrapper — PlaylistBuilder does the real work)
//// Owns: translating CompletedSegmentFile and init-segment notifications
////       into PlaylistBuilder calls.
//// The coordinator calls this; nothing else does.
//// =============================================================================

//public sealed class PlaylistWriter
//{
//    private readonly Recording.HLS.HlsPlaylist _builder;

//    public PlaylistWriter(Recording.HLS.HlsPlaylist builder) => _builder = builder;

//    public void RegisterInitSegment(string fileName) => _builder.AddInitSegment(fileName);

//    public void RegisterSegment(CompletedSegmentFile file) =>
//        _builder.AddSegment(file.FileName, file.TotalDuration);

//    public void RegisterDiscontinuity() => _builder.AddDiscontinuity();

//    public Task FinalizeAsync(CancellationToken ct = default) => _builder.FinalizeAsync(ct);
//}

//// =============================================================================
//// Coordinator — RecordingPipeline
//// Owns: the recording loop, sequence cursor, retry policy,
////       wiring stages together, flush/finalize decisions.
//// The only class that knows all four stages exist.
//// =============================================================================

//public sealed class RecordingPipeline(
//    ManifestSource manifestSource,
//    SegmentAccumulator accumulator,
//    PlaylistWriter writer,
//    int maxEmptyPolls,
//    ILogger logger)
//{
//    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
//    private static readonly TimeSpan EmptyPollDelay = TimeSpan.FromSeconds(10);
//    private static readonly TimeSpan NetworkErrorDelay = TimeSpan.FromSeconds(1);
//    private const int MaxConsecutiveNetworkErrors = 5;
//    private long _lastMediaSequence = -1;

//    /// <summary>
//    /// Runs until the stream ends, is cancelled, or too many consecutive failures occur.
//    /// </summary>
//    public async Task RunAsync(CancellationToken ct)
//    {
//        int emptyPollsRemaining = maxEmptyPolls;
//        int consecutiveNetErrors = 0;

//        while (!ct.IsCancellationRequested && emptyPollsRemaining > 0)
//        {
//            try
//            {
//                var (manifest, qualityChanged) = await manifestSource.GetNextManifestAsync(ct);

//                if (manifest is null)
//                {
//                    logger.LogWarning("No manifest available. Retrying… ({Remaining} attempts left)", --emptyPollsRemaining);
//                    await Task.Delay(EmptyPollDelay, ct);
//                    continue;
//                }

//                emptyPollsRemaining = maxEmptyPolls;
//                consecutiveNetErrors = 0;

//                var parsed = SegmentFilter.Parse(manifest, _lastMediaSequence);
//                _lastMediaSequence = parsed.LastMediaSequence;

//                if (qualityChanged)
//                {
//                    await FlushAndMarkDiscontinuityAsync();
//                    logger.LogDebug("Quality switch detected; discontinuity added.");
//                }

//                if (parsed.InitSegmentUrl is { } initUrl && !accumulator.InitSegmentDownloaded)
//                    writer.RegisterInitSegment(Path.GetFileName(initUrl));

//                await foreach (var completedFile in accumulator.AccumulateAsync(parsed, ct))
//                    writer.RegisterSegment(completedFile);

//                if (parsed.IsStreamEnded)
//                {
//                    logger.LogInformation("Stream ended gracefully.");
//                    break;
//                }

//                await Task.Delay(PollInterval, ct);
//            }
//            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
//            {
//                consecutiveNetErrors++;
//                logger.LogWarning("Network hiccup #{Count}.", consecutiveNetErrors);

//                if (consecutiveNetErrors > MaxConsecutiveNetworkErrors)
//                    throw;

//                await Task.Delay(NetworkErrorDelay, ct);
//            }
//        }
//    }

//    /// <summary>
//    /// Seals whatever is in progress and closes the playlist.
//    /// Call this after <see cref="RunAsync"/> completes (normally or via cancellation).
//    /// </summary>
//    public async Task FinalizeAsync()
//    {
//        await FlushAndMarkDiscontinuityAsync();
//        await writer.FinalizeAsync();
//    }

//    private async Task FlushAndMarkDiscontinuityAsync()
//    {
//        var flushed = await accumulator.FlushAsync();
//        if (flushed is not null)
//            writer.RegisterSegment(flushed);
//        writer.RegisterDiscontinuity();
//    }
//}

//// =============================================================================
//// Parsers (pure static helpers — no state, no dependencies)
//// =============================================================================

//public static class MasterPlaylistParser
//{
//    public static MediaPlaylist[] ParseVariants(string masterPlaylist)
//    {
//        var lines = masterPlaylist.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
//        var variants = new List<MediaPlaylist>();

//        for (int i = 0; i < lines.Length - 1; i++)
//        {
//            var line = lines[i].Trim();

//            if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal))
//                continue;

//            var bandwidthStr = HlsTagReader.ReadTagValue(line, "BANDWIDTH", ',');
//            if (!int.TryParse(bandwidthStr, out var bandwidth))
//                continue;

//            variants.Add(new(bandwidth, lines[i + 1].Trim()));
//            i++; // consume the URL line
//        }

//        return [.. variants.OrderBy(v => v.Bandwidth)];
//    }
//}