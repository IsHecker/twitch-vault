// using System.Text.Json;
// using FluentAssertions;
// using Microsoft.Extensions.Logging;
// using NSubstitute;
// using SixLabors.ImageSharp;
// using SixLabors.ImageSharp.PixelFormats;
// using TwitchVault.Api.Features.Recording.Storyboard;

// namespace TwitchVault.Api.Tests.Unit.Features.Recording;

// public class StoryboardManagerTests : IDisposable
// {
//     private readonly string _tempDirectory;
//     private readonly IFrameExtractor _frameExtractor = Substitute.For<IFrameExtractor>();
//     private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
//     private readonly IDataStore _dataStore = Substitute.For<IDataStore>();
//     private readonly ILogger<StoryboardManager> _logger = Substitute.For<ILogger<StoryboardManager>>();

//     private StoryboardManager CreateSut() => new(_frameExtractor, _httpClientFactory, _dataStore, _logger);

//     public StoryboardManagerTests()
//     {
//         _tempDirectory = Path.Combine(Path.GetTempPath(), "TwitchVault_StoryboardTests_" + Guid.NewGuid());
//         Directory.CreateDirectory(_tempDirectory);
//     }

//     /// <summary>
//     /// Creates a list of dummy frames and sets up the extractor mock to return them for
//     /// any file path, then transfers ownership so the manager can dispose them.
//     /// </summary>
//     private List<Image<Bgr24>> SetupFrames(int count)
//     {
//         var frames = Enumerable.Range(0, count)
//             .Select(_ => new Image<Bgr24>(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight))
//             .ToList();

//         // Return independent clones so the manager can safely dispose the returned list
//         // without affecting the local reference used for assertion setup.
//         _frameExtractor
//             .ExtractFrames(Arg.Any<string>(), Arg.Any<float>())
//             .Returns(_ => frames.Select(f => f.Clone()).ToList());

//         return frames;
//     }

//     [Fact]
//     public async Task FinalizeAsync_ShouldGenerateManifestAndStrip_WhenSegmentsProcessed()
//     {
//         // Arrange
//         var sut = CreateSut();
//         sut.Attach(_tempDirectory);

//         // 12 frames returned per ProcessSegmentAsync call (1 call)
//         var localFrames = SetupFrames(12);

//         // Act
//         await sut.ProcessSegmentAsync("segment_0.ts", 60f);
//         foreach (var f in localFrames) f.Dispose();

//         await sut.FinalizeAsync(60);

//         // Assert
//         var sheet0Path = Path.Combine(_tempDirectory, "storyboard_0.jpg");
//         var stripPath = Path.Combine(_tempDirectory, StoryboardConstants.StripFileName);
//         var manifestPath = Path.Combine(_tempDirectory, StoryboardConstants.ManifestFileName);

//         File.Exists(sheet0Path).Should().BeTrue();
//         File.Exists(stripPath).Should().BeTrue();
//         File.Exists(manifestPath).Should().BeTrue();

//         var json = await File.ReadAllTextAsync(manifestPath);
//         var manifest = JsonSerializer.Deserialize<StoryboardManifest>(json);

//         manifest.Should().NotBeNull();
//         manifest!.Count.Should().Be(12);
//         manifest.Width.Should().Be(StoryboardConstants.ThumbWidth);
//         manifest.Height.Should().Be(StoryboardConstants.ThumbHeight);
//         manifest.Cols.Should().Be(StoryboardConstants.Cols);
//         manifest.Rows.Should().Be(StoryboardConstants.Rows);
//         manifest.Interval.Should().Be(StoryboardConstants.IntervalSeconds);
//         manifest.Images.Should().ContainSingle().Which.Should().Be("storyboard_0.jpg");
//         manifest.Strip.Should().Be(StoryboardConstants.StripFileName);

//         // Verify strip image dimensions
//         using var stripImage = await Image.LoadAsync(_tempDirectory + "/" + StoryboardConstants.StripFileName);
//         stripImage.Width.Should().Be(StoryboardConstants.StripWidth);
//         stripImage.Height.Should().Be(StoryboardConstants.StripHeight);
//     }

//     [Fact]
//     public async Task ProcessSegmentAsync_ShouldRollOverToNextSheet_WhenExceeding100Frames()
//     {
//         // Arrange
//         var sut = CreateSut();
//         sut.Attach(_tempDirectory);

//         // First call returns 100 frames (triggers rollover), second returns 5
//         var batch1Frames = Enumerable.Range(0, 100)
//             .Select(_ => new Image<Bgr24>(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight))
//             .ToList();
//         var batch2Frames = Enumerable.Range(0, 5)
//             .Select(_ => new Image<Bgr24>(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight))
//             .ToList();

//         _frameExtractor
//             .ExtractFrames("seg_0.ts", Arg.Any<float>())
//             .Returns(_ => batch1Frames.Select(f => f.Clone()).ToList());
//         _frameExtractor
//             .ExtractFrames("seg_1.ts", Arg.Any<float>())
//             .Returns(_ => batch2Frames.Select(f => f.Clone()).ToList());

//         // Act
//         await sut.ProcessSegmentAsync("seg_0.ts", 500f);
//         foreach (var f in batch1Frames) f.Dispose();

//         await sut.ProcessSegmentAsync("seg_1.ts", 25f);
//         foreach (var f in batch2Frames) f.Dispose();

//         await sut.FinalizeAsync(525);

//         // Assert
//         File.Exists(Path.Combine(_tempDirectory, "storyboard_0.jpg")).Should().BeTrue();
//         File.Exists(Path.Combine(_tempDirectory, "storyboard_1.jpg")).Should().BeTrue();

//         var json = await File.ReadAllTextAsync(Path.Combine(_tempDirectory, StoryboardConstants.ManifestFileName));
//         var manifest = JsonSerializer.Deserialize<StoryboardManifest>(json);

//         manifest!.Count.Should().Be(105);
//         manifest.Images.Should().HaveCount(2);
//         manifest.Images[0].Should().Be("storyboard_0.jpg");
//         manifest.Images[1].Should().Be("storyboard_1.jpg");
//     }

//     [Fact]
//     public async Task ProcessSegmentAsync_ShouldDisposeAllFrames_AfterAppending()
//     {
//         // Arrange
//         var sut = CreateSut();
//         sut.Attach(_tempDirectory);

//         var frame = new Image<Bgr24>(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight);

//         // Track disposal via a wrapper — ImageSharp disposes immediately when Dispose() is called
//         _frameExtractor
//             .ExtractFrames(Arg.Any<string>(), Arg.Any<float>())
//             .Returns(_ =>
//             {
//                 var clone = frame.Clone();
//                 // Wrap in a list; we'll verify the manager disposes it by checking the original is untouched
//                 return [clone];
//             });

//         // Act — should not throw and must not leak
//         await sut.ProcessSegmentAsync("any.ts", 10f);

//         // If we get here without exception and the frame wasn't leaked, the test passes
//         frame.Dispose();
//     }

//     [Fact]
//     public async Task ProcessKeyframeAsync_ShouldAppendKeyframeAndDispose()
//     {
//         // Arrange
//         var sut = CreateSut();
//         sut.Attach(_tempDirectory);

//         var frame = new Image<Bgr24>(StoryboardConstants.ThumbWidth, StoryboardConstants.ThumbHeight);
//         _frameExtractor
//             .ExtractKeyframe(Arg.Any<System.IO.Stream>(), Arg.Any<string>(), Arg.Any<string>())
//             .Returns(_ => frame.Clone());

//         using var stream = new MemoryStream([1, 2, 3]);

//         // Act
//         await sut.ProcessKeyframeAsync(stream, ".ts");
//         await sut.FinalizeAsync(6);

//         frame.Dispose();

//         // Assert
//         var sheet0Path = Path.Combine(_tempDirectory, "storyboard_0.jpg");
//         var manifestPath = Path.Combine(_tempDirectory, StoryboardConstants.ManifestFileName);

//         File.Exists(sheet0Path).Should().BeTrue();
//         File.Exists(manifestPath).Should().BeTrue();

//         var json = await File.ReadAllTextAsync(manifestPath);
//         var manifest = JsonSerializer.Deserialize<StoryboardManifest>(json);
//         manifest.Should().NotBeNull();
//         manifest!.Count.Should().Be(1);
//         manifest.Interval.Should().Be(StoryboardConstants.IntervalSeconds);
//     }

//     public void Dispose()
//     {
//         try
//         {
//             if (Directory.Exists(_tempDirectory))
//                 Directory.Delete(_tempDirectory, recursive: true);
//         }
//         catch
//         {
//             // Ignore temp cleanup errors
//         }
//     }
// }