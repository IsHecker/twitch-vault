using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using TwitchVault.Api.Configuration;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace TwitchVault.Api.Features.Recording.Storyboard;

public sealed class StoryboardCompiler(
    IFrameExtractor frameExtractor,
    IHttpClientFactory httpClientFactory,
    IDataStore dataStore,
    IOptions<VaultOptions> vaultOptions,
    ILogger<StoryboardCompiler> logger)
{
    private const string InitSegmentFileName = $"{HlsSegmentNaming.InitPrefix}.mp4";

    // Sheet, strip and progress are written together every N tiles (4 rows).
    // A crash loses at most this much work; a graceful stop loses none (see CaptureFramesAsync).
    private const int CheckpointEveryTiles = 4 * StoryboardConstants.Cols;

    private int DownloadIntervalMs => vaultOptions.Value.StoryboardDownloadIntervalMs;
    private long _lastDownloadTickMs = 0;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = StoryboardConstants.JpegQuality
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task CompileAsync(Streams.Stream stream, CancellationToken cancellationToken = default)
    {
        var playlistPath = stream.Folder.PlaylistPath;
        if (!File.Exists(playlistPath))
        {
            logger.LogWarning("Cannot compile storyboard for stream '{StreamId}': playlist not found at '{Path}'.",
                stream.Id, playlistPath);
            await MarkStreamAsFinishedAsync(stream);
            return;
        }

        try
        {
            var header = await HlsPlaylistReader.ReadHeaderAsync(playlistPath, cancellationToken);
            if (header.TotalSeconds <= 0)
            {
                logger.LogWarning("Skipping storyboard for stream '{StreamId}': playlist reports non-positive duration ({Duration}s).",
                    stream.Id, header.TotalSeconds);
                await MarkStreamAsFinishedAsync(stream);
                return;
            }

            var session = await CreateSessionAsync(stream, header, cancellationToken);
            using (session)
            {
                await CaptureFramesAsync(playlistPath, session, cancellationToken);
                await WriteOutputsAsync(session, cancellationToken);

                logger.LogInformation(
                    "Compiled storyboard for stream '{StreamId}': {Frames} frames across {Sheets} sheets, strip with {StripCount} frames.",
                    stream.Id, session.Progress.TotalFrames, session.Progress.CurrentSheetIndex, session.Progress.StripFramesCaptured);
            }

            CleanupFiles(session.StreamDirectory, full: true);
            await MarkStreamAsFinishedAsync(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storyboard compilation failed for stream '{StreamId}'.", stream.Id);
            throw;
        }
        finally
        {
            frameExtractor.Reset();
            Interlocked.Exchange(ref _lastDownloadTickMs, 0);
        }
    }

    private async Task<CompilationSession> CreateSessionAsync(
        Streams.Stream stream, PlaylistHeader header, CancellationToken cancellationToken)
    {
        var streamDirectory = stream.Folder.AbsolutePath;
        var progressPath = Path.Combine(streamDirectory, StoryboardCompilationProgress.ProgressFileName);

        var interval = StoryboardCalculator.CalculateStoryboardInterval(header.TotalSeconds);
        var stripTimestamps = StoryboardCalculator.CalculateStripTimestamps(header.TotalSeconds);

        await DownloadInitSegmentAsync(header.InitUrl, streamDirectory, cancellationToken);
        var progress = await LoadOrCreateProgressAsync(progressPath, cancellationToken);
        var sheet = await LoadOrCreateSheetAsync(streamDirectory, progress.CurrentSheetIndex, cancellationToken);
        var strip = await LoadOrCreateStripSheetAsync(streamDirectory, stripTimestamps.Count, cancellationToken);

        return new CompilationSession(
            streamDirectory,
            progressPath,
            interval,
            header.InitUrl is not null ? ".mp4" : ".ts",
            stripTimestamps,
            progress,
            sheet,
            strip);
    }

    private async Task DownloadInitSegmentAsync(
        string? remoteInitUrl,
        string streamDirectory,
        CancellationToken cancellationToken)
    {
        if (remoteInitUrl is null)
            return;

        var initPath = Path.Combine(streamDirectory, InitSegmentFileName);
        if (File.Exists(initPath))
            return;

        await TryDownloadFileAsync(remoteInitUrl!, initPath, cancellationToken);
    }

    private async Task CaptureFramesAsync(string playlistPath, CompilationSession session, CancellationToken cancellationToken)
    {
        var progress = session.Progress;

        try
        {
            await foreach (var segment in HlsPlaylistReader.ReadSegmentsAsync(playlistPath, cancellationToken))
            {
                if (segment.Index <= progress.LastProcessedSegmentIndex)
                    continue;

                await ProcessSegmentAsync(session, segment, cancellationToken);

                progress.AccumulatedSeconds += segment.Duration;
                progress.LastProcessedSegmentIndex = segment.Index;
            }
        }
        catch (OperationCanceledException)
        {
            // Persist what we have so a restart resumes here instead of from the last checkpoint.
            await CheckpointAsync(session, sheetFull: false, CancellationToken.None);
            throw;
        }
    }

    private async Task ProcessSegmentAsync(
        CompilationSession session, PlaylistSegment segment, CancellationToken cancellationToken)
    {
        var progress = session.Progress;
        var segmentEndSeconds = progress.AccumulatedSeconds + segment.Duration;

        var isStripTarget = progress.StripFramesCaptured < session.StripTimestamps.Count
            && session.StripTimestamps[progress.StripFramesCaptured] < segmentEndSeconds;

        var isStoryboardTarget = segmentEndSeconds >= progress.NextCaptureSeconds;

        if (!isStripTarget && !isStoryboardTarget)
            return;

        var localSegmentPath = await DownloadSegmentAsync(session, segment, cancellationToken);
        if (localSegmentPath is null)
            return;

        if (isStripTarget)
            CaptureStripFrames(session, localSegmentPath, segment.Duration);

        if (isStoryboardTarget)
            await CaptureStoryboardTilesAsync(session, localSegmentPath, segment.Duration, cancellationToken);
    }

    private void CaptureStripFrames(CompilationSession session, string segmentPath, int segmentDuration)
    {
        var progress = session.Progress;
        var segmentEndSeconds = progress.AccumulatedSeconds + segmentDuration;

        while (progress.StripFramesCaptured < session.StripTimestamps.Count
            && session.StripTimestamps[progress.StripFramesCaptured] < segmentEndSeconds)
        {
            var targetTimestamp = session.StripTimestamps[progress.StripFramesCaptured];
            var targetSecond = Math.Max(0, targetTimestamp - progress.AccumulatedSeconds);
            var x = progress.StripFramesCaptured * StoryboardConstants.StripFrameWidth;

            var captured = frameExtractor.ExtractFrameInto(
                segmentPath,
                targetSecond,
                StoryboardConstants.StripFrameWidth,
                StoryboardConstants.StripFrameHeight,
                session.Strip,
                x,
                0);

            progress.StripFramesCaptured++;

            if (!captured)
                break;

            // Saved by the next checkpoint, together with the progress file.
            session.StripDirty = true;
        }
    }

    private async Task CaptureStoryboardTilesAsync(
        CompilationSession session, string segmentPath, int segmentDuration, CancellationToken cancellationToken)
    {
        var progress = session.Progress;

        while (progress.NextCaptureSeconds <= progress.AccumulatedSeconds + segmentDuration)
        {
            var target = (progress.NextCaptureSeconds - progress.AccumulatedSeconds) % segmentDuration;
            var origin = GetTileOrigin(progress.TilesOnCurrentSheet);

            var captured = frameExtractor.ExtractFrameInto(
                segmentPath,
                target,
                StoryboardConstants.ThumbWidth,
                StoryboardConstants.ThumbHeight,
                session.Sheet,
                origin.X,
                origin.Y);

            progress.NextCaptureSeconds += session.Interval;

            if (!captured)
                continue;

            progress.TilesOnCurrentSheet++;
            progress.TotalFrames++;
            session.SheetDirty = true;
            session.TilesSinceCheckpoint++;

            var sheetFull = progress.TilesOnCurrentSheet >= StoryboardConstants.TilesPerSheet;
            if (sheetFull || session.TilesSinceCheckpoint >= CheckpointEveryTiles)
            {
                await CheckpointAsync(session, sheetFull, cancellationToken);
                CleanupFiles(session.StreamDirectory, full: false, activeSegmentPath: segmentPath);
            }
        }
    }

    /// <summary>
    /// Writes everything that changed since the last checkpoint. Images first, progress LAST:
    /// the progress file is the commit record, so it must never describe work that isn't on disk yet
    /// (for example strip frames that are missing from strip.jpg).
    /// </summary>
    private static async Task CheckpointAsync(
        CompilationSession session, bool sheetFull, CancellationToken cancellationToken)
    {
        if (session.SheetDirty || sheetFull)
            await SaveCurrentSheetAsync(session, cancellationToken);

        if (session.StripDirty)
            await SaveStripAsync(session, cancellationToken);

        if (sheetFull)
            session.StartNewSheet();   // bumps CurrentSheetIndex in memory; persisted by the save below

        await SaveProgressAsync(session, cancellationToken);

        session.SheetDirty = false;
        session.StripDirty = false;
        session.TilesSinceCheckpoint = 0;
    }

    private static async Task WriteOutputsAsync(CompilationSession session, CancellationToken cancellationToken)
    {
        var progress = session.Progress;

        if (progress.TilesOnCurrentSheet > 0)
        {
            await SaveCurrentSheetAsync(session, cancellationToken);
            progress.CurrentSheetIndex++;
            progress.TilesOnCurrentSheet = 0;
        }

        if (progress.StripFramesCaptured > 0)
            await SaveStripAsync(session, cancellationToken);

        await WriteManifestAsync(
            session,
            progress.TotalFrames,
            progress.CurrentSheetIndex,
            cancellationToken);
    }

    private static async Task WriteManifestAsync(
        CompilationSession session,
        int totalFrames,
        int savedSheetCount,
        CancellationToken cancellationToken)
    {
        var sheetFileNames = Enumerable
            .Range(0, savedSheetCount)
            .Select(StoryboardConstants.FormatSheetFileName)
            .ToList();

        var manifest = new StoryboardManifest(
            Storyboard: new StoryboardInfo(
                Count: totalFrames,
                Width: StoryboardConstants.ThumbWidth,
                Height: StoryboardConstants.ThumbHeight,
                Columns: StoryboardConstants.Cols,
                Rows: StoryboardConstants.Rows,
                Interval: session.Interval,
                Files: sheetFileNames),
            Strip: new StripInfo(
                File: StoryboardConstants.StripFileName,
                Count: session.Progress.StripFramesCaptured,
                Width: StoryboardConstants.StripFrameWidth,
                Height: StoryboardConstants.StripFrameHeight,
                Columns: session.StripTimestamps.Count,
                Rows: 1));

        var manifestPath = Path.Combine(session.StreamDirectory, StoryboardConstants.ManifestFileName);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);
    }

    private static Task SaveStripAsync(CompilationSession session, CancellationToken cancellationToken)
    {
        return WriteJpegAsync(session.Strip, GetStripSheetPath(session.StreamDirectory), cancellationToken);
    }

    private static async Task<StoryboardCompilationProgress> LoadOrCreateProgressAsync(
        string progressPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(progressPath))
            return StoryboardCompilationProgress.Create();

        try
        {
            var json = await File.ReadAllTextAsync(progressPath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<StoryboardCompilationProgress>(json);
            if (loaded is not null)
                return loaded;
        }
        catch { }

        return StoryboardCompilationProgress.Create();
    }

    private static Task SaveProgressAsync(CompilationSession session, CancellationToken cancellationToken) =>
        WriteJsonAsync(session.ProgressPath, session.Progress, cancellationToken);

    private static async Task<Image<Bgr24>> LoadOrCreateSheetAsync(
        string streamDirectory, int sheetIndex, CancellationToken cancellationToken)
    {
        var sheetPath = GetSheetPath(streamDirectory, sheetIndex);
        if (!File.Exists(sheetPath))
            return CreateBlankSheet();

        return await Image.LoadAsync<Bgr24>(sheetPath, cancellationToken);
    }

    private static async Task<Image<Bgr24>> LoadOrCreateStripSheetAsync(
        string streamDirectory, int frameCount, CancellationToken cancellationToken)
    {
        var stripPath = GetStripSheetPath(streamDirectory);
        var width = Math.Max(1, frameCount) * StoryboardConstants.StripFrameWidth;
        var height = StoryboardConstants.StripHeight;

        if (!File.Exists(stripPath))
            return new Image<Bgr24>(width, height);

        return await Image.LoadAsync<Bgr24>(stripPath, cancellationToken);
    }

    private static Task SaveCurrentSheetAsync(CompilationSession session, CancellationToken cancellationToken)
    {
        var sheetPath = GetSheetPath(session.StreamDirectory, session.Progress.CurrentSheetIndex);
        return WriteJpegAsync(session.Sheet, sheetPath, cancellationToken);
    }

    private static string GetSheetPath(string streamDirectory, int sheetIndex) =>
        Path.Combine(streamDirectory, StoryboardConstants.FormatSheetFileName(sheetIndex));

    private static string GetStripSheetPath(string streamDirectory) =>
        Path.Combine(streamDirectory, StoryboardConstants.StripFileName);

    private static Image<Bgr24> CreateBlankSheet() =>
        new(StoryboardConstants.SheetWidth, StoryboardConstants.SheetHeight);

    private static Point GetTileOrigin(int tileIndex)
    {
        var col = tileIndex % StoryboardConstants.Cols;
        var row = tileIndex / StoryboardConstants.Cols;

        return new Point(col * StoryboardConstants.ThumbWidth, row * StoryboardConstants.ThumbHeight);
    }

    private static async Task WriteAsync(string path, Func<string, Task> writeToTempFile)
    {
        var tempPath = $"{path}.tmp";
        await writeToTempFile(tempPath);
        File.Move(tempPath, path, overwrite: true);
    }

    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        WriteAsync(path, async tempPath =>
        {
            await using var stream = File.Create(tempPath);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        });

    private static Task WriteJpegAsync(Image image, string path, CancellationToken cancellationToken) =>
        WriteAsync(path, tempPath => image.SaveAsJpegAsync(tempPath, JpegEncoder, cancellationToken));

    private async Task<string?> DownloadSegmentAsync(
        CompilationSession session, PlaylistSegment segment, CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(session.StreamDirectory, $"temp_seg_{segment.Index}{session.SegmentExtension}");
        if (File.Exists(localPath))
            return localPath;

        if (!await TryDownloadFileAsync(segment.Url, localPath, cancellationToken))
            return null;

        return localPath;
    }

    private async Task<bool> TryDownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            await ThrottleDownloadAsync(cancellationToken);
            var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Segment download failed — skipping. URL: '{Url}', Destination: '{DestinationPath}', Status: {StatusCode}, Body: {Body}",
                    url, destinationPath, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return false;
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await contentStream.CopyToAsync(fileStream, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Network error downloading segment from '{Url}' to '{Path}'.", url, destinationPath);
            return false;
        }
    }

    private async Task ThrottleDownloadAsync(CancellationToken cancellationToken)
    {
        var nowMs = Environment.TickCount64;
        var elapsed = nowMs - Interlocked.Read(ref _lastDownloadTickMs);
        var remaining = DownloadIntervalMs - elapsed;
        if (remaining > 0)
            await Task.Delay((int)remaining, cancellationToken);

        Interlocked.Exchange(ref _lastDownloadTickMs, Environment.TickCount64);
    }

    private static void CleanupFiles(string directory, bool full = false, string? activeSegmentPath = null)
    {
        var activeFileName = activeSegmentPath is not null ? Path.GetFileName(activeSegmentPath) : null;

        var downloadedFiles = new DirectoryInfo(directory)
            .EnumerateFiles()
            .Where(f => (f.FullName.Contains("temp_") && (activeFileName is null || !string.Equals(f.Name, activeFileName, StringComparison.OrdinalIgnoreCase)))
                || (full && (f.FullName.Contains(HlsSegmentNaming.InitPrefix)
                || f.FullName.Contains(StoryboardCompilationProgress.ProgressFileName))))
            .ToList();

        foreach (var file in downloadedFiles)
        {
            File.Delete(file.FullName);
        }
    }

    private async Task MarkStreamAsFinishedAsync(Streams.Stream stream)
    {
        await dataStore.ExecuteAsync(() =>
        {
            stream.MarkAsFinished();
            dataStore.Save(stream);
            return Task.CompletedTask;
        });
    }

    private sealed record CompilationSession(
        string StreamDirectory,
        string ProgressPath,
        int Interval,
        string SegmentExtension,
        List<int> StripTimestamps,
        StoryboardCompilationProgress Progress,
        Image<Bgr24> Sheet,
        Image<Bgr24> Strip) : IDisposable
    {
        public bool SheetDirty { get; set; }
        public bool StripDirty { get; set; }
        public int TilesSinceCheckpoint { get; set; }

        public void StartNewSheet()
        {
            // Reuse the canvas instead of disposing and reallocating it. Cleared so the unused slots of a
            // final partial sheet stay black instead of showing the previous sheet's tiles.
            Sheet.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                    accessor.GetRowSpan(y).Clear();
            });

            Progress.CurrentSheetIndex++;
            Progress.TilesOnCurrentSheet = 0;
            SheetDirty = false;
        }

        public void Dispose()
        {
            Sheet.Dispose();
            Strip.Dispose();
        }
    }
}

internal readonly record struct PlaylistHeader(double TotalSeconds, string InitUrl);

internal readonly record struct PlaylistSegment(int Index, int Duration, string Url);

internal static class HlsPlaylistReader
{
    public static async Task<PlaylistHeader> ReadHeaderAsync(string playlistPath, CancellationToken cancellationToken)
    {
        double totalSeconds = 0;
        string initUrl = null!;

        using var reader = OpenPlaylist(playlistPath);

        ReadOnlyMemory<char> line;
        while (!(line = (await reader.ReadLineAsync(cancellationToken)).AsMemory().Trim()).IsEmpty)
        {
            if (line.Span.StartsWith(HlsTags.TotalSecondsPrefix, StringComparison.Ordinal))
            {
                var value = HlsTagReader.ReadTagValue(line.Span, HlsTags.TotalSecondsPrefix);
                if (double.TryParse(value, out var seconds))
                    totalSeconds = seconds;
            }
            else if (line.Span.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
            {
                var value = HlsTagReader.ReadTagValue(line.Span, HlsTags.MapPrefix).Trim('"');
                if (!string.IsNullOrEmpty(value))
                    initUrl = value;
            }
            else if (line.Span.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
            {
                break;
            }
        }

        return new PlaylistHeader(totalSeconds, initUrl);
    }

    public static async IAsyncEnumerable<PlaylistSegment> ReadSegmentsAsync(
        string playlistPath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = OpenPlaylist(playlistPath);

        var segmentIndex = -1;
        int? pendingDuration = null;

        ReadOnlyMemory<char> line;
        while (!(line = (await reader.ReadLineAsync(cancellationToken)).AsMemory().Trim()).IsEmpty)
        {
            if (line.IsEmpty)
                continue;

            if (line.Equals(HlsTags.EndList))
                yield break;

            if (line.Span.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
            {
                var value = HlsTagReader.ReadTagValue(line.Span, HlsTags.ExtInfPrefix).TrimEnd(',');
                if (float.TryParse(value, out var duration))
                    pendingDuration = (int)duration;
                continue;
            }

            if (pendingDuration.HasValue && line.Span.StartsWith("http"))
            {
                var segmentDuration = pendingDuration.Value;
                pendingDuration = null;

                yield return new PlaylistSegment(++segmentIndex, segmentDuration, Url: line.ToString());
            }
        }
    }

    private static StreamReader OpenPlaylist(string playlistPath) =>
        new(new FileStream(playlistPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
}