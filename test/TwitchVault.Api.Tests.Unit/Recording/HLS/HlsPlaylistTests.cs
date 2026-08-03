using System.Text;
using FluentAssertions;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class HlsPlaylistTests
{
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IStorageService _fileSystem = Substitute.For<IStorageService>();
    private readonly DateTime _testStartTime = new(2026, 7, 1, 12, 0, 0);
    private readonly MemoryStream _memoryStream = new();

    public HlsPlaylistTests()
    {
        _dateTimeProvider.DateTimeNow.Returns(_testStartTime);
        _fileSystem.Exists(Arg.Any<string>()).Returns(false);
        _fileSystem.OpenWrite(Arg.Any<string>(), Arg.Any<FileMode>()).Returns(_memoryStream);
    }

    [Fact]
    public async Task LoadOrCreateAsync_ShouldWriteHeader_WhenPlaylistDoesNotExist()
    {
        // Act
        await using (var playlist = await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem))
        {
            // The playlist writes to stream during initialization
        }

        // Assert
        var rawContent = Encoding.UTF8.GetString(_memoryStream.ToArray());
        rawContent.Should().Contain("#EXTM3U");
        rawContent.Should().Contain("#EXT-X-VERSION:6");
        rawContent.Should().Contain($"#ID3-EQUIV-TDTG:{_testStartTime:yyyy-MM-ddTHH:mm:ss}");
        rawContent.Should().Contain("#EXT-X-MEDIA-SEQUENCE:1");
    }

    [Fact]
    public async Task AddSegmentAsync_ShouldAppendSegmentToStream_WhenParametersAreValid()
    {
        // Act
        await using (var playlist = await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem))
        {
            await playlist.AddSegmentAsync("new_seg.ts", 5.234f);
        }

        // Assert
        var rawContent = Encoding.UTF8.GetString(_memoryStream.ToArray());
        rawContent.Should().Contain("#EXTINF:5.234,");
        rawContent.Should().Contain("#EXT-X-TARGETDURATION:006");
        rawContent.Should().Contain("new_seg.ts");
        rawContent.Should().Contain("#EXT-X-TOTAL-SECS:000000000005.234");
    }

    [Fact]
    public async Task AddDiscontinuityAsync_ShouldAppendDiscontinuity_WhenSegmentsHaveBeenFlushed()
    {
        // Act
        await using (var playlist = await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem))
        {
            await playlist.AddSegmentAsync("seg_1.ts", 6.0f);
            await playlist.AddDiscontinuityAsync();
        }

        // Assert
        var rawContent = Encoding.UTF8.GetString(_memoryStream.ToArray());
        rawContent.Should().Contain("#EXT-X-DISCONTINUITY");
    }

    [Fact]
    public async Task SetInitSegmentAsync_ShouldThrowInvalidOperationException_WhenInitSegmentAlreadySet()
    {
        // Act 
        await using var playlist = await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem);
        await playlist.SetInitSegmentAsync("init.mp4");
        var act = async () => await playlist.SetInitSegmentAsync("init_duplicate.mp4");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FinalizeAsync_ShouldAppendEndListTag_WhenPlaylistIsDisposed()
    {
        // Act
        await using (var playlist = await HlsPlaylist.LoadOrCreateAsync("test-folder", _dateTimeProvider, _fileSystem))
        {
        }

        // Assert
        var rawContent = Encoding.UTF8.GetString(_memoryStream.ToArray());
        rawContent.Should().Contain("#EXT-X-ENDLIST");
    }
}