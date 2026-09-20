using FluentAssertions;

namespace TwitchVault.Api.Tests.Unit.Common;

public class HlsTagReaderTests
{
    private readonly string _playlist;

    public HlsTagReaderTests()
    {
        _playlist = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Samples", "Playlist.m3u8"));
    }

    [Theory]
    [HlsJsonData]
    public void ReadTagValue_ShouldReturnCorrectValue_WhenTagExists(string tag, char endChar, string expected)
    {
        // Act
        var value = HlsTagReader.ReadTagValue(_playlist, tag, endChar);

        // Assert
        value.Should().Be(expected);
    }

    [Theory]
    [HlsJsonData]
    public void ReadTagValue_ShouldReturnEmpty_WhenTagDoesntExist(string tag, char endChar)
    {
        // Act
        var value = HlsTagReader.ReadTagValue(_playlist, tag, endChar);

        // Assert
        value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TAG:VALUE", "MISSING")]
    [InlineData("", "TAG")]
    [InlineData("ONLY_TAG_NO_COLON", "ONLY_TAG_NO_COLON")]
    public void ReadTagValue_ShouldNotThrow_OnMalformedInput(string playlist, string tag)
    {
        // Act
        var act = () => HlsTagReader.ReadTagValue(playlist, tag);

        // Assert
        act.Should().NotThrow();
    }
}