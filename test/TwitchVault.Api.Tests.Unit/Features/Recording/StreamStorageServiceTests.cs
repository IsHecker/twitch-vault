using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PolyStore;
using Stream = TwitchVault.Api.Features.Streams.Stream;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

public class StreamStorageServiceTests : IDisposable
{
    private readonly IPolyStore _polyStore = Substitute.For<IPolyStore>();
    private readonly IDataStore _dataStore = Substitute.For<IDataStore>();
    private readonly string _tempRoot;

    public StreamStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"TwitchVault_StreamStorage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _dataStore.ExecuteAsync(Arg.Any<Func<Task>>())
            .Returns(call => ((Func<Task>)call[0])());
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldReturnFalseAndSetDeleteFailed_WhenRemoteDeleteFails()
    {
        // Arrange
        var channelName = "testchannel_failure";
        var streamFolder = StreamFolder.Create(Path.Combine(_tempRoot, "Streams"), channelName);
        var streamLocalDir = streamFolder.AbsolutePath;
        Directory.CreateDirectory(streamLocalDir);

        await File.WriteAllTextAsync(streamFolder.RemoteUrlsPath, "seg_0.ts\thttps://cdn.discordapp.com/attachments/123/456/seg_0.ts\n");

        var stream = Stream.Create(
            "stream_failed_delete",
            "chan_id",
            streamFolder,
            DateTime.UtcNow,
            "Title",
            "Category");
        stream.SetStorageLocation(StorageLocation.Remote);
        stream.SetStorageInstance("discord-node-2");

        _polyStore.DeleteAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(PolyStore.Common.Results.Error.Failure("DeleteFailed", "Delete failed"));

        var sut = new StreamStorageService(_polyStore, _dataStore, NullLogger<StreamStorageService>.Instance);

        // Act
        var result = await sut.DeleteStreamAsync(stream);

        // Assert
        result.Should().BeFalse();
        stream.StorageOperationStatus.Should().Be(StorageOperationStatus.DeleteFailed);
        Directory.Exists(streamLocalDir).Should().BeTrue();
        File.Exists(streamFolder.RemoteUrlsPath).Should().BeTrue();
        _dataStore.Received(1).Save(stream);
        await _dataStore.DidNotReceive().DeleteAsync<Stream, string>(stream.Id);
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldReturnTrueAndDeleteRecord_WhenRemoteDeleteSucceeds()
    {
        // Arrange
        var channelName = "testchannel_success";
        var streamFolder = StreamFolder.Create(Path.Combine(_tempRoot, "Streams"), channelName);
        var streamLocalDir = streamFolder.AbsolutePath;
        Directory.CreateDirectory(streamLocalDir);

        await File.WriteAllTextAsync(streamFolder.RemoteUrlsPath, "seg_0.ts\thttps://cdn.discordapp.com/attachments/123/456/seg_0.ts\n");

        var stream = Stream.Create(
            "stream_success_delete",
            "chan_id",
            streamFolder,
            DateTime.UtcNow,
            "Title",
            "Category");
        stream.SetStorageLocation(StorageLocation.Remote);
        stream.SetStorageInstance("discord-node-2");

        _polyStore.DeleteAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(PolyStore.Common.Results.Result.Success);

        var sut = new StreamStorageService(_polyStore, _dataStore, NullLogger<StreamStorageService>.Instance);

        // Act
        var result = await sut.DeleteStreamAsync(stream);

        // Assert
        result.Should().BeTrue();
        Directory.Exists(streamLocalDir).Should().BeFalse();
        await _dataStore.Received(1).DeleteAsync<Stream, string>(stream.Id);
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