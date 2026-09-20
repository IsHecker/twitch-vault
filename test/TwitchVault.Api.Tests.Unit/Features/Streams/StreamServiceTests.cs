using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording;
using FluentAssertions;
using TwitchVault.Api.Common;
using DomainStream = TwitchVault.Api.Domain.Stream;
using TwitchVault.Api.Persistence.Database;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly ILogger<StreamService> _logger = Substitute.For<ILogger<StreamService>>();

    public StreamServiceTests()
    {
        _dataStore = new EfDataStore(_factory);
        _pathsOptions.Value.Returns(new PathsOptions { Streams = "Streams" });
    }

    private StreamService CreateSut() =>
        new(_dataStore, _dateTimeProvider, _pathsOptions, _logger);

    [Fact]
    public async Task DeleteStreamAsync_ShouldNotDelete_WhenStreamDoesNotExist()
    {
        // Act
        var sut = CreateSut();
        var result = await sut.DeleteStreamAsync("non-existent");

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(StorageOperationStatus.DeleteRequest)]
    [InlineData(StorageOperationStatus.Deleting)]
    [InlineData(StorageOperationStatus.DeleteFailed)]
    public async Task DeleteStreamAsync_ShouldReturnNotFound_WhenStreamIsAlreadyDeleteRelated(StorageOperationStatus status)
    {
        // Arrange
        var channelId = "channel-1";
        var streamId = "stream-123";
        var channel = Channel.Create(channelId, "testchannel", 1, isArchived: false);
        var stream = DomainStream.Create(streamId, channelId, StreamFolder.Create("Streams", "testchannel"), DateTime.UtcNow, "Title", "Cat");
        stream.SetStorageOperationStatus(status);

        using (var dbInit = _factory.CreateDbContext())
        {
            dbInit.Channels.Add(channel);
            dbInit.Streams.Add(stream);
            dbInit.SaveChanges();
        }

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteStreamAsync(streamId);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldSucceed_WhenChaptersIsEmpty()
    {
        // Arrange
        var channelId = "channel-1";
        var streamId = "stream-123";
        var channel = Channel.Create(channelId, "testchannel", 1, isArchived: false);
        var stream = DomainStream.Create(streamId, channelId, StreamFolder.Create("Streams", "testchannel"), DateTime.UtcNow, "Title", "Cat");
        stream.MarkAsFinished(DateTime.UtcNow);

        using (var dbInit = _factory.CreateDbContext())
        {
            dbInit.Channels.Add(channel);
            dbInit.Streams.Add(stream);
            dbInit.SaveChanges();
        }

        var sut = CreateSut();

        // Act
        var act = () => sut.DeleteStreamAsync(streamId);

        // Assert
        await act.Should().NotThrowAsync();

        using var dbCheck = _factory.CreateDbContext();
        var dbStream = dbCheck.Streams.First(s => s.Id == streamId);
        dbStream.StorageOperationStatus.Should().Be(StorageOperationStatus.DeleteRequest);
    }

    [Fact]
    public async Task ResetStaleStreamsAsync_ShouldUpdateStaleStreams_WhenStaleStreamsExist()
    {
        // Arrange
        var channelId = "channel-1";
        var channel = Channel.Create(channelId, "testchannel",1, isArchived: false);
        var staleStream = DomainStream.Create("stale-1", channelId, StreamFolder.Create("Streams", "testchannel"), DateTime.UtcNow, "Title", "Cat");
        var activeStreamId = "active-1";
        var activeStream = DomainStream.Create(activeStreamId, channelId, StreamFolder.Create("Streams", "testchannel"), DateTime.UtcNow, "Title", "Cat");

        using (var dbInit = _factory.CreateDbContext())
        {
            dbInit.Channels.Add(channel);
            dbInit.Streams.AddRange(staleStream, activeStream);
            dbInit.SaveChanges();
        }

        var sut = CreateSut();

        // Act
        await sut.ResetStaleStreamsAsync(channelId, activeStreamId);

        // Assert
        using var dbCheck = _factory.CreateDbContext();
        var dbStale = dbCheck.Streams.First(s => s.Id == "stale-1");
        var dbActive = dbCheck.Streams.First(s => s.Id == activeStreamId);

        dbStale.Status.Should().Be(StreamStatus.Finished);
        dbStale.FinishedAt.Should().NotBeNull();

        dbActive.Status.Should().Be(StreamStatus.Recording);
    }
}