using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PolyStore;
using TwitchVault.Api.Features.Recording;
using TwitchVault.Api.Features.Streams;
using Stream = TwitchVault.Api.Features.Streams.Stream;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

public class StreamStorageServiceTests : IDisposable
{
    private readonly IPolyStore _polyStore = Substitute.For<IPolyStore>();
    private readonly IDataStore _dataStore = Substitute.For<IDataStore>();
    private readonly IWebHostEnvironment _env = Substitute.For<IWebHostEnvironment>();
    private readonly string _tempRoot;

    public StreamStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"TwitchVault_StreamStorage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _env.ContentRootPath.Returns(_tempRoot);

        _dataStore.ExecuteAsync(Arg.Any<Func<Task>>())
            .Returns(call => ((Func<Task>)call[0])());
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldDeleteParentChannelDirectory_WhenNoOtherStreamsRemain()
    {
        // Arrange
        var channelName = "testchannel";
        var streamFolder = StreamFolder.Create("Streams", channelName);
        var streamLocalDir = streamFolder.GetAbsolutePath(_tempRoot);
        Directory.CreateDirectory(streamLocalDir);

        var parentChannelDir = Path.GetDirectoryName(streamLocalDir)!;

        var stream = Stream.Create(
            "stream_clean_parent",
            "chan_id",
            streamFolder,
            DateTime.UtcNow,
            "Title",
            "Category");

        var sut = new StreamStorageService(_polyStore, _dataStore, _env, NullLogger<StreamStorageService>.Instance);

        // Act
        var result = await sut.DeleteStreamAsync(stream);

        // Assert
        result.Should().BeTrue();
        Directory.Exists(streamLocalDir).Should().BeFalse();
        Directory.Exists(parentChannelDir).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldKeepParentChannelDirectory_WhenOtherStreamsRemain()
    {
        // Arrange
        var channelName = "testchannel_multiple";
        var streamFolder1 = StreamFolder.Create("Streams", channelName);
        var streamLocalDir1 = streamFolder1.GetAbsolutePath(_tempRoot);
        Directory.CreateDirectory(streamLocalDir1);

        var parentChannelDir = Path.GetDirectoryName(streamLocalDir1)!;
        var otherStreamDir = Path.Combine(parentChannelDir, "other_stream_folder");
        Directory.CreateDirectory(otherStreamDir);

        var stream = Stream.Create(
            "stream_keep_parent",
            "chan_id",
            streamFolder1,
            DateTime.UtcNow,
            "Title",
            "Category");

        var sut = new StreamStorageService(_polyStore, _dataStore, _env, NullLogger<StreamStorageService>.Instance);

        // Act
        var result = await sut.DeleteStreamAsync(stream);

        // Assert
        result.Should().BeTrue();
        Directory.Exists(streamLocalDir1).Should().BeFalse();
        Directory.Exists(parentChannelDir).Should().BeTrue();
        Directory.Exists(otherStreamDir).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}