// using System.Globalization;
// using System.Text.Json;
// using SixLabors.ImageSharp;
// using SixLabors.ImageSharp.Formats.Jpeg;
// using SixLabors.ImageSharp.PixelFormats;
// using SixLabors.ImageSharp.Processing;
// using TwitchVault.Api.Features.Recording.HLS;

// namespace TwitchVault.Api.Features.Recording.Storyboard;

// public sealed class StoryboardManager(
//     IFrameExtractor frameExtractor,
//     IHttpClientFactory httpClientFactory,
//     IDataStore dataStore,
//     ILogger<StoryboardManager> logger) : IStoryboardManager
// {
//     private static readonly JpegEncoder SheetEncoder = new()
//     {
//         Quality = StoryboardConstants.JpegQuality
//     };

//     private static readonly JsonSerializerOptions ManifestJsonOptions = new()
//     {
//         WriteIndented = true
//     };

//     private string? _streamDirectory;
//     private Image<Bgr24>? _currentSheet;
//     private int _savedSheetCount;
//     private int _tilesOnCurrentSheet;
//     private int _totalFrames;
//     private bool _isFinalized;

//     public void Attach(string streamDirectory) => _streamDirectory = streamDirectory;

//     public async Task ProcessSegmentAsync(
//         string filePath,
//         float durationSeconds,
//         CancellationToken cancellationToken = default)
//     {
//         if (_streamDirectory is not { } directory || _isFinalized)
//             return;

//         var frames = frameExtractor.ExtractFrames(filePath, durationSeconds);

//         try
//         {
//             foreach (var frame in frames)
//             {
//                 AddTile(frame);

//                 if (_tilesOnCurrentSheet >= StoryboardConstants.TilesPerSheet)
//                     await SaveCurrentSheetAsync(directory, cancellationToken);
//             }
//         }
//         finally
//         {
//             foreach (var frame in frames)
//                 frame.Dispose();
//         }
//     }

//     public async Task ProcessKeyframeAsync(
//         System.IO.Stream segmentStream,
//         string extension,
//         CancellationToken cancellationToken = default)
//     {
//         if (_streamDirectory is not { } directory || _isFinalized)
//             return;

//         var frame = frameExtractor.ExtractKeyframe(segmentStream, extension, directory);
//         if (frame is null)
//             return;

//         try
//         {
//             AddTile(frame);

//             if (_tilesOnCurrentSheet >= StoryboardConstants.TilesPerSheet)
//                 await SaveCurrentSheetAsync(directory, cancellationToken);
//         }
//         finally
//         {
//             frame.Dispose();
//         }
//     }

//     public async Task FinalizeAsync(double totalDurationSeconds, CancellationToken cancellationToken = default)
//     {
//         if (_streamDirectory is not { } directory || _isFinalized)
//             return;

//         _isFinalized = true;

//         try
//         {
//             // Flush the last, partially filled sheet (no-op if there isn't one).
//             await SaveCurrentSheetAsync(directory, cancellationToken);

//             if (_totalFrames == 0)
//             {
//                 logger.LogInformation("No storyboard frames captured for '{Dir}'. Skipping manifest.", directory);
//                 return;
//             }

//             await GenerateStripAsync(directory, cancellationToken);
//             await WriteManifestAsync(directory, cancellationToken);

//             logger.LogInformation(
//                 "Storyboard finalized for '{Dir}'. Total frames: {Frames}, Sheets: {Sheets}",
//                 directory, _totalFrames, _savedSheetCount);
//         }
//         catch (Exception ex)
//         {
//             logger.LogError(ex, "Failed to finalize storyboard in '{Dir}'.", directory);
//         }
//         finally
//         {
//             DiscardCurrentSheet();
//         }
//     }

//     // ---------- Post-Processing Single-Pass Compilation ----------

//     public async Task CompileAsync(Streams.Stream stream, CancellationToken cancellationToken = default)
//     {
//         var playlistPath = stream.Folder.PlaylistPath;
//         if (!File.Exists(playlistPath))
//         {
//             logger.LogWarning("Cannot compile storyboard for stream '{StreamId}': playlist not found at '{Path}'.",
//                 stream.Id, playlistPath);
//             return;
//         }

//         var directory = stream.Folder.AbsolutePath;
//         Attach(directory);

//         var progressPath = Path.Combine(directory, StoryboardCompilationProgress.ProgressFileName);
//         var downloadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

//         try
//         {
//             var (totalSeconds, remoteInitUrl) = await ReadPlaylistHeaderAsync(playlistPath, cancellationToken);
//             if (totalSeconds <= 0)
//             {
//                 logger.LogWarning("Stream '{StreamId}' has non-positive duration ({Duration}). Skipping storyboard compilation.",
//                     stream.Id, totalSeconds);
//                 await PromoteToFinishedAsync(stream);
//                 return;
//             }

//             var interval = StoryboardCalculator.CalculateStoryboardInterval(totalSeconds);
//             var estimatedSegments = StoryboardCalculator.EstimateSegmentCount(totalSeconds);
//             var stripIndices = StoryboardCalculator.CalculateStripSegmentIndices(estimatedSegments).ToHashSet();

//             var progress = await LoadOrCreateProgressAsync(progressPath, interval, cancellationToken);

//             // Restore state from progress
//             _savedSheetCount = progress.CurrentSheetIndex;
//             _tilesOnCurrentSheet = progress.TilesOnCurrentSheet;
//             _totalFrames = progress.TotalFrames;

//             var initPath = Path.Combine(directory, "init.mp4");
//             if (!string.IsNullOrEmpty(remoteInitUrl) && !File.Exists(initPath))
//             {
//                 var downloaded = await TryDownloadFileAsync(remoteInitUrl, initPath, cancellationToken);
//                 if (downloaded)
//                     downloadedFiles.Add(initPath);
//             }

//             _currentSheet = await TryLoadSheetAsync(directory, _savedSheetCount, cancellationToken)
//                 ?? new Image<Bgr24>(StoryboardConstants.SheetWidth, StoryboardConstants.SheetHeight);

//             var stripFrames = new List<Image<Bgr24>>();

//             try
//             {
//                 using var fileStream = new FileStream(playlistPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
//                 using var reader = new StreamReader(fileStream);

//                 var currentSegmentIndex = -1;
//                 float? pendingDuration = null;
//                 string? line;

//                 while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
//                 {
//                     line = line.Trim();
//                     if (string.IsNullOrEmpty(line))
//                         continue;

//                     if (line.Equals(HlsTags.EndList, StringComparison.Ordinal))
//                         break;

//                     if (line.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
//                     {
//                         var durationStr = HlsTagReader.ReadTagValue(line, HlsTags.ExtInfPrefix).TrimEnd(',');
//                         if (float.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration))
//                         {
//                             pendingDuration = parsedDuration;
//                         }
//                         continue;
//                     }

//                     if (pendingDuration.HasValue && !line.StartsWith('#'))
//                     {
//                         currentSegmentIndex++;
//                         var segmentDuration = pendingDuration.Value;
//                         pendingDuration = null;
//                         var segmentUrl = line;

//                         if (currentSegmentIndex <= progress.LastProcessedSegmentIndex)
//                             continue;

//                         var isStripTarget = stripIndices.Contains(currentSegmentIndex);
//                         var isStoryboardTarget = (progress.AccumulatedSeconds + segmentDuration >= progress.NextCaptureSeconds);

//                         if (isStripTarget || isStoryboardTarget)
//                         {
//                             var localSegmentPath = await EnsureSegmentDownloadedAsync(
//                                 segmentUrl, currentSegmentIndex, directory, downloadedFiles, cancellationToken);

//                             if (localSegmentPath is not null && File.Exists(localSegmentPath))
//                             {
//                                 if (isStripTarget)
//                                 {
//                                     var stripFrame = frameExtractor.ExtractStripFrameFromFile(localSegmentPath);
//                                     if (stripFrame is not null)
//                                     {
//                                         stripFrames.Add(stripFrame);
//                                     }
//                                 }

//                                 if (isStoryboardTarget)
//                                 {
//                                     while (progress.AccumulatedSeconds + segmentDuration >= progress.NextCaptureSeconds)
//                                     {
//                                         using var thumbnail = frameExtractor.ExtractThumbnailFromFile(localSegmentPath);
//                                         if (thumbnail is not null)
//                                         {
//                                             AddTile(thumbnail);
//                                             progress.NextCaptureSeconds += interval;

//                                             // Sheet full (100 tiles)
//                                             if (_tilesOnCurrentSheet >= StoryboardConstants.TilesPerSheet)
//                                             {
//                                                 await SaveCurrentSheetAsync(directory, cancellationToken);
//                                                 progress.CurrentSheetIndex = _savedSheetCount;
//                                                 progress.TilesOnCurrentSheet = _tilesOnCurrentSheet;
//                                                 progress.TotalFrames = _totalFrames;
//                                                 await SaveProgressAsync(progressPath, progress, cancellationToken);
//                                             }
//                                             // Row complete (every 5 tiles)
//                                             else if (_tilesOnCurrentSheet % StoryboardConstants.Cols == 0)
//                                             {
//                                                 await FlushCurrentSheetToDiskAsync(directory, cancellationToken);
//                                                 progress.CurrentSheetIndex = _savedSheetCount;
//                                                 progress.TilesOnCurrentSheet = _tilesOnCurrentSheet;
//                                                 progress.TotalFrames = _totalFrames;
//                                                 await SaveProgressAsync(progressPath, progress, cancellationToken);
//                                             }
//                                         }
//                                         else
//                                         {
//                                             progress.NextCaptureSeconds += interval;
//                                         }
//                                     }
//                                 }
//                             }
//                         }

//                         progress.AccumulatedSeconds += segmentDuration;
//                         progress.LastProcessedSegmentIndex = currentSegmentIndex;
//                     }
//                 }

//                 // Finalize remaining partial sheet
//                 if (_tilesOnCurrentSheet > 0)
//                 {
//                     await SaveCurrentSheetAsync(directory, cancellationToken);
//                 }

//                 // Write manifest
//                 await WriteManifestAsync(directory, (int)Math.Round(interval), cancellationToken);

//                 // Assemble strip in memory and save
//                 if (stripFrames.Count > 0)
//                 {
//                     await AssembleAndSaveStripAsync(stripFrames, directory, cancellationToken);
//                 }

//                 logger.LogInformation(
//                     "Compiled storyboard for stream '{StreamId}': {Frames} frames across {Sheets} sheets, strip with {StripCount} frames.",
//                     stream.Id, _totalFrames, _savedSheetCount, stripFrames.Count);
//             }
//             finally
//             {
//                 DiscardCurrentSheet();
//                 foreach (var f in stripFrames)
//                     f.Dispose();
//             }

//             // Cleanup downloaded files and progress file
//             CleanupFiles(downloadedFiles, progressPath);

//             // Transition stream to Finished
//             await PromoteToFinishedAsync(stream);
//         }
//         catch (Exception ex)
//         {
//             logger.LogError(ex, "Error occurred during storyboard compilation for stream '{StreamId}'.", stream.Id);
//             throw;
//         }
//     }

//     private static async Task<(double TotalSeconds, string? RemoteInitUrl)> ReadPlaylistHeaderAsync(
//         string playlistPath, CancellationToken cancellationToken)
//     {
//         double totalSeconds = 0;
//         string? remoteInitUrl = null;

//         using var stream = new FileStream(playlistPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
//         using var reader = new StreamReader(stream);

//         string? line;
//         while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
//         {
//             line = line.Trim();
//             if (line.StartsWith(HlsTags.TotalSecondsPrefix, StringComparison.Ordinal))
//             {
//                 var val = HlsTagReader.ReadTagValue(line, HlsTags.TotalSecondsPrefix);
//                 if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
//                     totalSeconds = secs;
//             }
//             else if (line.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
//             {
//                 var val = HlsTagReader.ReadTagValue(line, HlsTags.MapPrefix).Trim('"');
//                 if (!string.IsNullOrEmpty(val))
//                     remoteInitUrl = val;
//             }
//             else if (line.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
//             {
//                 break;
//             }
//         }

//         return (totalSeconds, remoteInitUrl);
//     }

//     private static async Task<StoryboardCompilationProgress> LoadOrCreateProgressAsync(
//         string progressPath, double interval, CancellationToken cancellationToken)
//     {
//         if (File.Exists(progressPath))
//         {
//             try
//             {
//                 var json = await File.ReadAllTextAsync(progressPath, cancellationToken);
//                 var loaded = JsonSerializer.Deserialize<StoryboardCompilationProgress>(json);
//                 if (loaded is not null)
//                     return loaded;
//             }
//             catch
//             {
//                 // Restart fresh if corrupt
//             }
//         }

//         return StoryboardCompilationProgress.Create();
//     }

//     private static async Task SaveProgressAsync(
//         string progressPath, StoryboardCompilationProgress progress, CancellationToken cancellationToken)
//     {
//         var tempPath = $"{progressPath}.tmp";
//         await using (var stream = File.Create(tempPath))
//         {
//             await JsonSerializer.SerializeAsync(stream, progress, ManifestJsonOptions, cancellationToken);
//         }
//         File.Move(tempPath, progressPath, overwrite: true);
//     }

//     private async Task<string?> EnsureSegmentDownloadedAsync(
//         string segmentUrl,
//         int segmentIndex,
//         string streamDirectory,
//         HashSet<string> downloadedFiles,
//         CancellationToken cancellationToken)
//     {
//         var ext = Path.GetExtension(new Uri(segmentUrl, UriKind.RelativeOrAbsolute).LocalPath);
//         if (string.IsNullOrEmpty(ext))
//             ext = ".mp4";

//         var localFileName = $"temp_seg_{segmentIndex}{ext}";
//         var localPath = Path.Combine(streamDirectory, localFileName);

//         if (File.Exists(localPath))
//             return localPath;

//         var success = await TryDownloadFileAsync(segmentUrl, localPath, cancellationToken);
//         if (success)
//         {
//             downloadedFiles.Add(localPath);
//             return localPath;
//         }

//         return null;
//     }

//     private async Task<bool> TryDownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
//     {
//         try
//         {
//             var client = httpClientFactory.CreateClient();
//             using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
//             if (!response.IsSuccessStatusCode)
//                 return false;

//             await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
//             await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
//             await contentStream.CopyToAsync(fileStream, cancellationToken);
//             return true;
//         }
//         catch (Exception ex) when (ex is not OperationCanceledException)
//         {
//             logger.LogWarning(ex, "Failed to download segment from '{Url}' to '{Path}'.", url, destinationPath);
//             return false;
//         }
//     }

//     private async Task AssembleAndSaveStripAsync(
//         List<Image<Bgr24>> stripFrames, string streamDirectory, CancellationToken cancellationToken)
//     {
//         var totalWidth = stripFrames.Sum(f => f.Width);
//         var maxHeight = stripFrames.Max(f => f.Height);

//         using var stripImage = new Image<Bgr24>(totalWidth, maxHeight);
//         var xOffset = 0;

//         foreach (var frame in stripFrames)
//         {
//             stripImage.Mutate(ctx => ctx.DrawImage(frame, new Point(xOffset, 0), 1f));
//             xOffset += frame.Width;
//         }

//         var stripPath = Path.Combine(streamDirectory, StoryboardConstants.StripFileName);
//         var tempPath = $"{stripPath}.tmp";
//         await stripImage.SaveAsJpegAsync(tempPath, SheetEncoder, cancellationToken);
//         File.Move(tempPath, stripPath, overwrite: true);
//     }

//     private void CleanupFiles(HashSet<string> downloadedFiles, string progressPath)
//     {
//         foreach (var file in downloadedFiles)
//         {
//             try
//             {
//                 if (File.Exists(file))
//                     File.Delete(file);
//             }
//             catch (Exception ex)
//             {
//                 logger.LogWarning(ex, "Failed to delete downloaded temporary segment '{File}'.", file);
//             }
//         }

//         try
//         {
//             if (File.Exists(progressPath))
//                 File.Delete(progressPath);
//         }
//         catch (Exception ex)
//         {
//             logger.LogWarning(ex, "Failed to delete progress file '{File}'.", progressPath);
//         }
//     }

//     private async Task PromoteToFinishedAsync(Streams.Stream stream)
//     {
//         await dataStore.ExecuteAsync(() =>
//         {
//             stream.MarkAsFinished();
//             dataStore.Save(stream);
//             return Task.CompletedTask;
//         });
//     }

//     public ValueTask DisposeAsync()
//     {
//         DiscardCurrentSheet();
//         return ValueTask.CompletedTask;
//     }

//     // ---------- Sheets ----------

//     private void AddTile(Image<Bgr24> frame)
//     {
//         _currentSheet ??= new Image<Bgr24>(StoryboardConstants.SheetWidth, StoryboardConstants.SheetHeight);

//         var origin = GetTileOrigin(_tilesOnCurrentSheet);
//         _currentSheet.ProcessPixelRows(frame, (sheetAccessor, frameAccessor) =>
//         {
//             for (var y = 0; y < frameAccessor.Height; y++)
//             {
//                 var srcRow = frameAccessor.GetRowSpan(y);
//                 var dstRow = sheetAccessor.GetRowSpan(origin.Y + y).Slice(origin.X, frameAccessor.Width);
//                 srcRow.CopyTo(dstRow);
//             }
//         });

//         _tilesOnCurrentSheet++;
//         _totalFrames++;
//     }

//     private async Task FlushCurrentSheetToDiskAsync(string directory, CancellationToken cancellationToken)
//     {
//         if (_currentSheet is null)
//             return;

//         var path = Path.Combine(directory, StoryboardConstants.FormatSheetFileName(_savedSheetCount));
//         var tempPath = $"{path}.tmp";
//         await _currentSheet.SaveAsJpegAsync(tempPath, SheetEncoder, cancellationToken);
//         File.Move(tempPath, path, overwrite: true);
//     }

//     private async Task SaveCurrentSheetAsync(string directory, CancellationToken cancellationToken)
//     {
//         if (_currentSheet is null)
//             return;

//         var path = Path.Combine(directory, StoryboardConstants.FormatSheetFileName(_savedSheetCount));
//         var tempPath = $"{path}.tmp";
//         await _currentSheet.SaveAsJpegAsync(tempPath, SheetEncoder, cancellationToken);
//         File.Move(tempPath, path, overwrite: true);

//         DiscardCurrentSheet();
//         _savedSheetCount++;
//         _tilesOnCurrentSheet = 0;
//     }

//     private void DiscardCurrentSheet()
//     {
//         _currentSheet?.Dispose();
//         _currentSheet = null;
//     }

//     private static Point GetTileOrigin(int tileIndex) => new(
//         (tileIndex % StoryboardConstants.Cols) * StoryboardConstants.ThumbWidth,
//         (tileIndex / StoryboardConstants.Cols) * StoryboardConstants.ThumbHeight);

//     // ---------- Strip preview (fallback) ----------

//     private async Task GenerateStripAsync(string directory, CancellationToken cancellationToken)
//     {
//         using var strip = new Image<Bgr24>(StoryboardConstants.StripWidth, StoryboardConstants.StripHeight);
//         var frameIndices = GetStripFrameIndices();

//         Image<Bgr24>? sheet = null;
//         var loadedSheetIndex = -1;

//         try
//         {
//             for (var slot = 0; slot < frameIndices.Length; slot++)
//             {
//                 var frameIndex = frameIndices[slot];
//                 var sheetIndex = frameIndex / StoryboardConstants.TilesPerSheet;

//                 if (sheetIndex != loadedSheetIndex)
//                 {
//                     sheet?.Dispose();
//                     sheet = await TryLoadSheetAsync(directory, sheetIndex, cancellationToken);
//                     loadedSheetIndex = sheetIndex;
//                 }

//                 if (sheet is null)
//                     continue;

//                 var tileOrigin = GetTileOrigin(frameIndex % StoryboardConstants.TilesPerSheet);
//                 var tileBounds = new Rectangle(
//                     tileOrigin,
//                     new Size(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight));

//                 using var tile = sheet.Clone(ctx => ctx.Crop(tileBounds));

//                 var stripX = slot * StoryboardConstants.ThumbWidth;
//                 strip.Mutate(ctx => ctx.DrawImage(tile, new Point(stripX, 0), 1f));
//             }

//             var stripPath = Path.Combine(directory, StoryboardConstants.StripFileName);
//             var tempPath = $"{stripPath}.tmp";
//             await strip.SaveAsJpegAsync(tempPath, SheetEncoder, cancellationToken);
//             File.Move(tempPath, stripPath, overwrite: true);
//         }
//         finally
//         {
//             sheet?.Dispose();
//         }
//     }

//     private int[] GetStripFrameIndices()
//     {
//         var count = Math.Min(StoryboardConstants.StripFrameCount, _totalFrames);
//         var indices = new int[count];

//         for (var i = 0; i < count; i++)
//         {
//             var position = (i + 0.5) / count;
//             indices[i] = Math.Clamp((int)(position * _totalFrames), 0, _totalFrames - 1);
//         }

//         return indices;
//     }

//     private static async Task<Image<Bgr24>?> TryLoadSheetAsync(
//         string directory,
//         int sheetIndex,
//         CancellationToken cancellationToken)
//     {
//         var path = Path.Combine(directory, StoryboardConstants.FormatSheetFileName(sheetIndex));

//         return File.Exists(path)
//             ? await Image.LoadAsync<Bgr24>(path, cancellationToken)
//             : null;
//     }

//     // ---------- Manifest ----------

//     private Task WriteManifestAsync(string directory, CancellationToken cancellationToken)
//         => WriteManifestAsync(directory, StoryboardConstants.IntervalSeconds, cancellationToken);

//     private async Task WriteManifestAsync(string directory, int interval, CancellationToken cancellationToken)
//     {
//         var sheetFileNames = Enumerable
//             .Range(0, _savedSheetCount)
//             .Select(StoryboardConstants.FormatSheetFileName)
//             .ToList();

//         var manifest = new StoryboardManifest(
//             Count: _totalFrames,
//             Width: StoryboardConstants.ThumbWidth,
//             Height: StoryboardConstants.ThumbHeight,
//             Cols: StoryboardConstants.Cols,
//             Rows: StoryboardConstants.Rows,
//             Interval: interval,
//             Images: sheetFileNames,
//             Strip: StoryboardConstants.StripFileName);

//         var manifestPath = Path.Combine(directory, StoryboardConstants.ManifestFileName);
//         var tempPath = $"{manifestPath}.tmp";

//         await using (var stream = File.Create(tempPath))
//         {
//             await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, cancellationToken);
//         }
//         File.Move(tempPath, manifestPath, overwrite: true);
//     }
// }
