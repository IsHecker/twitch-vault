using System.Text;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class SegmentDownloaderTests
{
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly SettingsService _settingsService;
    private readonly SegmentStateTracker _segmentStateTracker;
    private readonly SegmentDownloader _sut;

    public SegmentDownloaderTests()
    {
        var pathsOptions = Substitute.For<IOptions<PathsOptions>>();
        pathsOptions.Value.Returns(new PathsOptions { Settings = "non_existent.json" });
        _settingsService = new SettingsService(pathsOptions);
        _settingsService.Settings.Vault.MaxSegmentDurationInSec = 10;

        _segmentStateTracker = new SegmentStateTracker(_settingsService);
        _sut = new SegmentDownloader(_twitchGqlClient, _segmentStateTracker, _fileSystem);
        _dateTimeProvider.DateTimeNow.Returns(DateTime.UtcNow);
    }

    private async Task<HlsPlaylist> CreateTestPlaylistAsync()
    {
        var mockPlaylistStream = new MemoryStream();
        _fileSystem.Exists(Arg.Any<string>()).Returns(false);
        _fileSystem.OpenWrite(Arg.Any<string>(), FileMode.OpenOrCreate).Returns(mockPlaylistStream);

        return await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem);
    }

    [Fact]
    public async Task DownloadSegmentsAsync_ShouldDownloadAndWriteInitSegment_WhenInitialTrackHasInitSegmentAndPlaylistHasNone()
    {
        // Arrange
        await using var playlist = await CreateTestPlaylistAsync();

        var parsedManifest = new ManifestExtractionResult
        {
            InitSegmentUrl = "https://example.com/init.mp4",
            Segments = []
        };

        var initStream = new MemoryStream(Encoding.UTF8.GetBytes("init-data"));
        var mockFileStream = new MemoryStream();

        _twitchGqlClient.DownloadAsStreamAsync("https://example.com/init.mp4", Arg.Any<CancellationToken>())
            .Returns(initStream);

        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("init.mp4")), FileMode.Create)
            .Returns(mockFileStream);

        // Act
        var resultList = new List<(string FileName, float Duration)>();
        await foreach (var item in _sut.DownloadSegmentsAsync("folder", parsedManifest, playlist))
        {
            resultList.Add(item);
        }

        // Assert
        resultList.Should().ContainSingle();
        resultList[0].FileName.Should().Be("init.mp4");
        resultList[0].Duration.Should().Be(0f);

        mockFileStream.ToArray().Should().BeEquivalentTo(Encoding.UTF8.GetBytes("init-data"));
    }

    [Fact]
    public async Task DownloadSegmentsAsync_ShouldAppendSegmentsToFileSystem_WhenSegmentsAreParsed()
    {
        // Arrange
        await using var playlist = await CreateTestPlaylistAsync();

        var parsedManifest = new ManifestExtractionResult
        {
            Segments =
            [
                new() { Url = "https://example.com/index-0.ts", Duration = 5.0f }
            ]
        };

        var segmentBytes = Encoding.UTF8.GetBytes("segment-content-0");
        var segmentStream = new MemoryStream(segmentBytes);
        var mockFileStream = new MemoryStream();

        _twitchGqlClient.DownloadAsStreamAsync("https://example.com/index-0.ts", Arg.Any<CancellationToken>())
            .Returns(segmentStream);

        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("seg_1.ts")), FileMode.Append)
            .Returns(mockFileStream);

        // Act
        var resultList = new List<(string FileName, float Duration)>();
        await foreach (var item in _sut.DownloadSegmentsAsync("folder", parsedManifest, playlist))
        {
            resultList.Add(item);
        }

        // Assert
        resultList.Should().BeEmpty(); // Less than MaxSegmentDurationInSec (10)
        _segmentStateTracker.AccumulatedDuration.Should().Be(5.0f);
        _segmentStateTracker.CurrentFileName.Should().Be("seg_1.ts");

        mockFileStream.ToArray().Should().BeEquivalentTo(segmentBytes);
    }

    [Fact]
    public async Task DownloadSegmentsAsync_ShouldYieldRotationSegment_WhenSegmentTrackerIsFull()
    {
        // Arrange
        await using var playlist = await CreateTestPlaylistAsync();

        var parsedManifest = new ManifestExtractionResult
        {
            Segments =
            [
                new() { Url = "https://example.com/index-0.ts", Duration = 12.0f }
            ]
        };

        var segmentBytes = Encoding.UTF8.GetBytes("segment-content-0");
        var segmentStream = new MemoryStream(segmentBytes);
        var mockFileStream = new MemoryStream();

        _twitchGqlClient.DownloadAsStreamAsync("https://example.com/index-0.ts", Arg.Any<CancellationToken>())
            .Returns(segmentStream);

        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("seg_1.ts")), FileMode.Append)
            .Returns(mockFileStream);

        // Act
        var resultList = new List<(string FileName, float Duration)>();
        await foreach (var item in _sut.DownloadSegmentsAsync("folder", parsedManifest, playlist))
        {
            resultList.Add(item);
        }

        // Assert
        resultList.Should().ContainSingle();
        resultList[0].FileName.Should().Be("seg_1.ts");
        resultList[0].Duration.Should().Be(12.0f);

        _segmentStateTracker.AccumulatedDuration.Should().Be(0f);
        _segmentStateTracker.CurrentFileName.Should().BeNull();
    }
}