using FluentAssertions;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class PlaylistStateRestorerTests
{
    [Fact]
    public void Restore_ShouldPopulateResultCorrectly_WhenPlaylistTagsAreValid()
    {
        // Arrange
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-TARGETDURATION:6",
            "#ID3-EQUIV-TDTG:2026-07-05T00:00:00",
            "#EXT-X-PLAYLIST-TYPE:EVENT",
            "#EXT-X-MEDIA-SEQUENCE:1",
            "#TWITCH-MEDIA-SEQUENCE:000000000042",
            "#EXT-X-TOTAL-SECS:000000000256.123",
            "#EXT-X-MAP:URI=\"init.mp4\"",
            "#EXTINF:6.000,",
            "seg_01.ts",
            "#EXTINF:6.000,",
            "seg_02.ts"
        };

        // Act
        var result = PlaylistStateRestorer.Restore(lines);

        // Assert
        result.Should().NotBeNull();
        result.TargetDuration.Should().Be(6.0f);
        result.TotalDuration.Should().Be(256.123f);
        result.StartTime.Should().BeCloseTo(new DateTime(2026, 7, 5, 0, 0, 0), TimeSpan.FromSeconds(1));
        result.HasInitSegment.Should().BeTrue();
        result.LastSegmentFileName.Should().Be("seg_02.ts");
        result.SegmentCount.Should().Be(42);
        result.IsFinalized.Should().BeFalse();
    }

    [Fact]
    public void Restore_ShouldSetIsFinalizedToTrue_WhenEndListTagIsPresent()
    {
        // Arrange
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-TARGETDURATION:6",
            "#EXTINF:6.000,",
            "seg_01.ts",
            "#EXT-X-ENDLIST"
        };

        // Act
        var result = PlaylistStateRestorer.Restore(lines);

        // Assert
        result.IsFinalized.Should().BeTrue();
    }

    [Fact]
    public void Restore_ShouldFallbackToCurrentTime_WhenStartTimeIsEmptyOrMalformed()
    {
        // Arrange
        var lines = new List<string>
        {
            "#EXTM3U",
            "#ID3-EQUIV-TDTG:NOT_A_VALID_DATE",
            "#EXT-X-TARGETDURATION:6"
        };
        var utcNowBefore = DateTime.Now;

        // Act
        var result = PlaylistStateRestorer.Restore(lines);

        // Assert
        result.StartTime.Should().BeOnOrAfter(utcNowBefore).And.BeOnOrBefore(DateTime.Now);
    }

    [Fact]
    public void Restore_ShouldSetHasInitSegmentToFalse_WhenNoInitSegmentTagIsPresent()
    {
        // Arrange
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-TARGETDURATION:6"
        };

        // Act
        var result = PlaylistStateRestorer.Restore(lines);

        // Assert
        result.HasInitSegment.Should().BeFalse();
    }

    [Fact]
    public void Restore_ShouldHaveNullLastSegment_WhenTrailingExtInfTagHasNoSegmentLine()
    {
        // Arrange
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXTINF:6.000," // Missing the filename on the next line
        };

        // Act
        var result = PlaylistStateRestorer.Restore(lines);

        // Assert
        result.LastSegmentFileName.Should().BeNull();
    }
}