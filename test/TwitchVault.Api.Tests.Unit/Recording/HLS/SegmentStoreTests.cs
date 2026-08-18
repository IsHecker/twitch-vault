using System.Text;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class SegmentStoreTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly SettingsService _settingsService;
    private readonly SegmentStore _sut;

    public SegmentStoreTests()
    {
        var pathsOptions = Substitute.For<IOptions<PathsOptions>>();
        pathsOptions.Value.Returns(new PathsOptions { Settings = "non_existent.json" });
        _settingsService = new SettingsService(pathsOptions);
        _settingsService.Settings.Vault.MaxSegmentDurationInSec = 10;

        _sut = new SegmentStore(_fileSystem, _settingsService);
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveInitSegment_WhenIsInitSegment()
    {
        // Arrange
        var initStream = new MemoryStream(Encoding.UTF8.GetBytes("init-data"));
        var mockFileStream = new MemoryStream();
        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("init.mp4")), FileMode.Create)
            .Returns(mockFileStream);

        var segment = new DownloadedSegment(new RemoteSegment("https://example.com/init.mp4", 0, IsInitSegment: true), initStream);

        // Act
        var result = await _sut.SaveAsync("test-folder", segment, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.FilePath.Should().Be("init.mp4");
        result.Duration.Should().Be(0f);
        mockFileStream.ToArray().Should().BeEquivalentTo(Encoding.UTF8.GetBytes("init-data"));
    }

    [Fact]
    public async Task SaveAsync_ShouldAppendSegmentAndReturnNull_WhenNotFull()
    {
        // Arrange
        var segmentBytes = Encoding.UTF8.GetBytes("segment-content-0");
        var segmentStream = new MemoryStream(segmentBytes);
        var mockFileStream = new MemoryStream();

        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("seg_1.ts")), FileMode.Append)
            .Returns(mockFileStream);

        var segment = new DownloadedSegment(new RemoteSegment("https://example.com/index-0.ts", 5.0f), segmentStream);

        // Act
        var result = await _sut.SaveAsync("test-folder", segment, null, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        mockFileStream.ToArray().Should().BeEquivalentTo(segmentBytes);
    }

    [Fact]
    public async Task SaveAsync_ShouldCloseAndReturnSegment_WhenFull()
    {
        // Arrange
        var segmentBytes = Encoding.UTF8.GetBytes("segment-content-0");
        var segmentStream = new MemoryStream(segmentBytes);
        var mockFileStream = new MemoryStream();

        _fileSystem.OpenWrite(Arg.Is<string>(s => s.EndsWith("seg_1.ts")), FileMode.Append)
            .Returns(mockFileStream);

        var segment = new DownloadedSegment(new RemoteSegment("https://example.com/index-0.ts", 12.0f), segmentStream);

        // Act
        var result = await _sut.SaveAsync("test-folder", segment, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.FilePath.Should().Be("seg_1.ts");
        result.Duration.Should().Be(12.0f);
    }
}