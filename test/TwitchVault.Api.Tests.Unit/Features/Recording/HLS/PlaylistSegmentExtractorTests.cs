using FluentAssertions;
using System.Text;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;


public class PlaylistSegmentExtractorTests
{
    private static ResponseStream ToResponseStream(string manifest)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);
        return new ResponseStream(new MemoryStream(bytes), HttpResponse: null);
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldReturnExpectedSegments_WhenManifestAndNewSegmentsAreValid()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:100\n" +
            "#EXTINF:2.000,\n" +
            "segment_100.ts\n" +
            "#EXTINF:4.123,\n" +
            "segment_101.ts\n";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 99, CancellationToken.None);

        // Assert
        result.Segments.Should().HaveCount(2);

        result.Segments[0].Url.Should().Be("segment_100.ts");
        result.Segments[0].Duration.Should().Be(2.000f);

        result.Segments[1].Url.Should().Be("segment_101.ts");
        result.Segments[1].Duration.Should().Be(4.123f);

        result.LastMediaSequence.Should().Be(101);
        result.IsStreamEnded.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldSkipSegments_WhenSequenceNumberIsLessThanOrEqualLastSequence()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:100\n" +
            "#EXTINF:2.000,\n" +
            "segment_100.ts\n" +
            "#EXTINF:2.000,\n" +
            "segment_101.ts\n" +
            "#EXTINF:2.000,\n" +
            "segment_102.ts\n";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 101, CancellationToken.None);

        // Assert
        result.Segments.Should().ContainSingle();
        result.Segments[0].Url.Should().Be("segment_102.ts");
        result.LastMediaSequence.Should().Be(102);
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldExtractInitSegmentUrl_WhenInitSegmentMapTagIsPresent()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:50\n" +
            "#EXT-X-MAP:URI=\"init_seq.mp4\"\n" +
            "#EXTINF:1.500,\n" +
            "segment_50.ts\n";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 49, CancellationToken.None);

        // Assert
        result.Segments.Should().HaveCount(2);
        result.Segments[0].Url.Should().Be("init_seq.mp4");
        result.Segments[0].IsInitSegment.Should().BeTrue();
        result.Segments[1].Url.Should().Be("segment_50.ts");
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldSetIsStreamEndedToTrue_WhenEndListTagIsPresent()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:1\n" +
            "#EXTINF:2.5,\n" +
            "segment_1.ts\n" +
            "#EXT-X-ENDLIST";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 0, CancellationToken.None);

        // Assert
        result.IsStreamEnded.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldReturnEmptyDataResult_WhenMediaSequenceTagIsMissing()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXTINF:2.000,\n" +
            "segment_1.ts\n";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 10, CancellationToken.None);

        // Assert
        result.Segments.Should().BeEmpty();
        result.LastMediaSequence.Should().Be(10);
        result.IsStreamEnded.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldReturnSegmentWithZeroDuration_WhenExtInfFormatIsUnexpected()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:1\n" +
            "#EXTINF:NOT_A_VALID_NUMBER,label\n" +
            "segment_1.ts\n";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 0, CancellationToken.None);

        // Assert
        result.Segments.Should().ContainSingle();
        result.Segments[0].Url.Should().Be("segment_1.ts");
        result.Segments[0].Duration.Should().Be(0f);
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldIgnoreTrailingExtInf_WhenFileEndsWithoutNewlineAndSegmentUrl()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:1\n" +
            "#EXTINF:2.000,";

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 0, CancellationToken.None);

        // Assert
        result.Segments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractNewSegmentsAsync_ShouldNotAddSegment_WhenFileEndsWithNewlineButNoSegmentUrlLine()
    {
        // Arrange
        var manifest =
            "#EXTM3U\n" +
            "#EXT-X-MEDIA-SEQUENCE:1\n" +
            "#EXTINF:2.000,\n"; // Ends with a trailing newline, no segment URL line follows

        // Act
        var result = await PlaylistSegmentExtractor.ExtractNewSegmentsAsync(
            ToResponseStream(manifest), lastMediaSequence: 0, CancellationToken.None);

        // Assert
        result.Segments.Should().BeEmpty();
        result.LastMediaSequence.Should().Be(1);
        result.IsStreamEnded.Should().BeFalse();
    }
}