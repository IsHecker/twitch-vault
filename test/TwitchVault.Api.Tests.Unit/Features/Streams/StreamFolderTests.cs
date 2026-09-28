using System.Text.Json;
using FluentAssertions;

namespace TwitchVault.Api.Tests.Unit.Features.Streams;

public class StreamFolderTests
{
    [Fact]
    public void Constructor_ShouldNormalizeBackslashesToForwardSlashes()
    {
        var folder = new StreamFolder(@"Streams\channel\2026-09-23 12-00-00");

        folder.RelativePath.Should().Be("Streams/channel/2026-09-23 12-00-00");
    }

    [Fact]
    public void AbsolutePath_ShouldResolveRelativeToCurrentDirectory()
    {
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        folder.AbsolutePath.Should().Be(Path.GetFullPath("Streams/testchan/2026-01-01"));
    }

    [Fact]
    public void GetAbsolutePath_WithCustomRoot_ShouldResolveRelativeToRoot()
    {
        var customRoot = @"C:\CustomRoot";
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        var abs = folder.GetAbsolutePath(customRoot);

        abs.Should().Be(Path.GetFullPath(Path.Combine(customRoot, "Streams/testchan/2026-01-01")));
    }

    [Fact]
    public void PlaylistPath_ShouldCombineWithAbsolutePath()
    {
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        folder.PlaylistPath.Should().Be(Path.Combine(folder.AbsolutePath, StreamFolder.PlaylistFile));
    }

    [Fact]
    public void RemoteUrlsPath_ShouldCombineWithAbsolutePath()
    {
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        folder.RemoteUrlsPath.Should().Be(Path.Combine(folder.AbsolutePath, StreamFolder.RemoteUrlsFile));
    }

    [Fact]
    public void ThumbnailPath_ShouldCombineWithRelativePathAndForwardSlashes()
    {
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        folder.ThumbnailPath.Should().Be("Streams/testchan/2026-01-01/thumbnail.jpg");
    }

    [Fact]
    public void JsonSerialization_ShouldSerializeAsPlainString()
    {
        var folder = new StreamFolder("Streams/testchan/2026-01-01");

        var json = JsonSerializer.Serialize(folder);

        json.Should().Be("\"Streams/testchan/2026-01-01\"");
    }

    [Fact]
    public void JsonDeserialization_ShouldDeserializeFromString()
    {
        var json = "\"Streams/testchan/2026-01-01\"";

        var folder = JsonSerializer.Deserialize<StreamFolder>(json);

        folder.RelativePath.Should().Be("Streams/testchan/2026-01-01");
    }

    [Fact]
    public void ImplicitConversions_ShouldWorkBothWays()
    {
        StreamFolder folder = "Streams/testchan/2026-01-01";
        string path = folder;

        folder.RelativePath.Should().Be("Streams/testchan/2026-01-01");
        path.Should().Be("Streams/testchan/2026-01-01");
    }

    [Fact]
    public void Create_ShouldSanitizeChannelName()
    {
        var folder = StreamFolder.Create("Streams", "test:chan/nel?*");

        folder.RelativePath.Should().NotContain(":");
        folder.RelativePath.Should().NotContain("?");
        folder.RelativePath.Should().NotContain("*");
        folder.RelativePath.Should().StartWith("Streams/test_chan_nel__/");
    }
}