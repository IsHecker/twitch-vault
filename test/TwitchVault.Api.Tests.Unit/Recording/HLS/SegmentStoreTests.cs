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
    private const string StreamFolderPath = "test-folder";
    private const string InitUrl = "https://example.com/init.mp4";
    private const string SegmentUrl = "https://example.com/index-0.ts";

    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
    private readonly SegmentStore _sut;

    public SegmentStoreTests()
    {
        _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxSegmentDurationInSec = 10 });
        _sut = new SegmentStore(_fileSystem, _vaultOptions);
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveInitSegment_WhenIsInitSegment()
    {
        // Arrange
        var expectedPath = Path.Combine(StreamFolderPath, "init.mp4");
        var mockFileStream = SetupWrite(expectedPath, FileMode.Create);
        var segment = CreateSegment(InitUrl, "init-data", duration: 0f, isInit: true);

        // Act
        var result = await _sut.SaveAsync(StreamFolderPath, segment, lastSegmentFileName: null, CancellationToken.None);

        // Assert
        AssertSavedSegment(result, expectedPath, expectedDuration: 0f, mockFileStream, "init-data");
    }

    [Fact]
    public async Task SaveAsync_ShouldAppendSegmentAndReturnNull_WhenNotFull()
    {
        // Arrange
        var expectedPath = Path.Combine(StreamFolderPath, "seg_1.ts");
        var mockFileStream = SetupWrite(expectedPath, FileMode.Create);
        var segment = CreateSegment(SegmentUrl, "segment-content-0", duration: 5f);

        // Act
        var result = await _sut.SaveAsync(StreamFolderPath, segment, lastSegmentFileName: null, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        mockFileStream.ToArray().Should().BeEquivalentTo(Encoding.UTF8.GetBytes("segment-content-0"));
    }

    [Fact]
    public async Task SaveAsync_ShouldCloseAndReturnSegment_WhenDurationReachesMax()
    {
        // Arrange
        var expectedPath = Path.Combine(StreamFolderPath, "seg_1.ts");
        var mockFileStream = SetupWrite(expectedPath, FileMode.Create);
        var segment = CreateSegment(SegmentUrl, "segment-content-0", duration: 12f);

        // Act
        var result = await _sut.SaveAsync(StreamFolderPath, segment, lastSegmentFileName: null, CancellationToken.None);

        // Assert
        AssertSavedSegment(result, expectedPath, expectedDuration: 12f, mockFileStream, "segment-content-0");
    }

    [Fact]
    public async Task SaveAsync_ShouldReuseSameFileStream_AcrossAppendsUntilFull()
    {
        // Arrange: two partial appends that together cross the 10s threshold.
        var expectedPath = Path.Combine(StreamFolderPath, "seg_1.ts");
        var mockFileStream = SetupWrite(expectedPath, FileMode.Create);
        var first = CreateSegment(SegmentUrl, "part-1-", duration: 6f);
        var second = CreateSegment(SegmentUrl, "part-2", duration: 6f);

        // Act
        var firstResult = await _sut.SaveAsync(StreamFolderPath, first, lastSegmentFileName: null, CancellationToken.None);
        var secondResult = await _sut.SaveAsync(StreamFolderPath, second, lastSegmentFileName: null, CancellationToken.None);

        // Assert
        firstResult.Should().BeNull();
        AssertSavedSegment(secondResult, expectedPath, expectedDuration: 12f, mockFileStream, "part-1-part-2");
        // The file should only be opened once and reused across both appends.
        _fileSystem.Received(1).OpenWrite(expectedPath, FileMode.Create);
    }

    [Fact]
    public void CloseCurrentSegment_ShouldReturnNull_WhenNoSegmentIsInProgress()
    {
        _sut.CloseCurrentSegment().Should().BeNull();
    }

    // --- shared helpers, so behavior changes to SegmentStore require one edit, not N ---

    private MemoryStream SetupWrite(string expectedPath, FileMode expectedMode)
    {
        var mockFileStream = new MemoryStream();
        _fileSystem.OpenWrite(expectedPath, expectedMode).Returns(mockFileStream);
        return mockFileStream;
    }

    private static SegmentContent CreateSegment(string url, string content, float duration, bool isInit = false) =>
        new(new RemoteSegment(url, duration, isInit), new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static void AssertSavedSegment(
        LocalSegment? result,
        string expectedPath,
        float expectedDuration,
        MemoryStream mockFileStream,
        string expectedWrittenContent)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expectedWrittenContent);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(expectedPath);
        result.Duration.Should().Be(expectedDuration);
        result.SizeBytes.Should().Be(expectedBytes.Length);
        mockFileStream.ToArray().Should().BeEquivalentTo(expectedBytes);
    }
}