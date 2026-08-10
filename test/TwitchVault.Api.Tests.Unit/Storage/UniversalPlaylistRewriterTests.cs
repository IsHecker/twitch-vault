using FluentAssertions;
using TwitchVault.Api.CloudStorage;

namespace TwitchVault.Api.Tests.Unit.Storage;

public class UniversalPlaylistRewriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _playlistPath;

    public UniversalPlaylistRewriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _playlistPath = Path.Combine(_tempDir, "playlist.m3u8");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RewritePlaylistSegmentsOnDiskAsync_ShouldReplaceLocalFilenamesWithRemoteUrls()
    {
        // Arrange
        var initialContent = """
            #EXTM3U
            #EXT-X-VERSION:6
            #EXT-X-TARGETDURATION:4
            #EXTINF:4.000,
            seg_1.ts
            #EXTINF:4.000,
            seg_2.ts
            #EXT-X-ENDLIST
            """;

        await File.WriteAllTextAsync(_playlistPath, initialContent);

        var uploaded = new List<UploadedSegment>
        {
            new("seg_1.ts", "https://cdn.example.com/attachments/111/222"),
            new("seg_2.ts", "https://cdn.example.com/attachments/111/333")
        };

        var rewriter = new UniversalPlaylistRewriter();

        // Act
        var result = await rewriter.RewritePlaylistSegmentsOnDiskAsync(_playlistPath, uploaded);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(_playlistPath);

        lines.Should().Contain("https://cdn.example.com/attachments/111/222");
        lines.Should().Contain("https://cdn.example.com/attachments/111/333");
        lines.Should().NotContain("segment_0001.ts");
    }
}
