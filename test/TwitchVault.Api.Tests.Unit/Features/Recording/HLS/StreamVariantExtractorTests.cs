using FluentAssertions;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class StreamVariantExtractorTests
{
    [Fact]
    public void ExtractVariants_ShouldReturnSortedVariants_WhenMasterPlaylistIsValid()
    {
        // Arrange
        var masterPlaylistContent =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=3422999,RESOLUTION=1280x720\n" +
            "https://example.com/720p.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=6807029,RESOLUTION=1920x1080\n" +
            "https://example.com/1080p.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=630000,RESOLUTION=640x360\n" +
            "https://example.com/360p.m3u8\n";

        // Act
        var result = StreamVariantExtractor.ExtractVariants(masterPlaylistContent);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);

        // Output should be sorted by bandwidth ascending
        result[0].Bandwidth.Should().Be(630000);
        result[0].Url.Should().Be("https://example.com/360p.m3u8");

        result[1].Bandwidth.Should().Be(3422999);
        result[1].Url.Should().Be("https://example.com/720p.m3u8");

        result[2].Bandwidth.Should().Be(6807029);
        result[2].Url.Should().Be("https://example.com/1080p.m3u8");
    }

    [Fact]
    public void ExtractVariants_ShouldReturnEmptyArray_WhenInputIsEmptyOrNull()
    {
        // Arrange & Act
        var resultNull = StreamVariantExtractor.ExtractVariants(string.Empty);

        // Assert
        resultNull.Should().NotBeNull();
        resultNull.Should().BeEmpty();
    }

    [Fact]
    public void ExtractVariants_ShouldParseCorrectly_WhenInputHasCarriageReturnNewlines()
    {
        // Arrange
        var masterPlaylistContent = "#EXTM3U\r\n#EXT-X-STREAM-INF:BANDWIDTH=500000\r\nhttps://example.com/stream.m3u8\r\n";

        // Act
        var result = StreamVariantExtractor.ExtractVariants(masterPlaylistContent);

        // Assert
        result.Should().ContainSingle();
        result[0].Bandwidth.Should().Be(500000);
        result[0].Url.Should().Be("https://example.com/stream.m3u8");
    }

    [Fact]
    public void ExtractVariants_ShouldSkipMalformedVariant_WhenBandwidthIsMalformed()
    {
        // Arrange
        var masterPlaylistContent =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=NOT_A_NUMBER,RESOLUTION=1280x720\n" +
            "https://example.com/invalid.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=1200000,RESOLUTION=1280x720\n" +
            "https://example.com/valid.m3u8\n";

        // Act
        var result = StreamVariantExtractor.ExtractVariants(masterPlaylistContent);

        // Assert
        result.Should().ContainSingle();
        result[0].Bandwidth.Should().Be(1200000);
        result[0].Url.Should().Be("https://example.com/valid.m3u8");
    }

    [Fact]
    public void ExtractVariants_ShouldIgnoreTrailingStreamInfo_WhenMissingUrlLine()
    {
        // Arrange
        var masterPlaylistContent =
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=1200000\n"; // No URL follows

        // Act
        var result = StreamVariantExtractor.ExtractVariants(masterPlaylistContent);

        // Assert
        result.Should().BeEmpty();
    }
}