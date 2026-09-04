using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Persistence.Database;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamFinalizerTests
{
    private readonly ITwitchGqlClient _twitchClient = Substitute.For<ITwitchGqlClient>();
    private readonly IStreamStorageService _storageService = Substitute.For<IStreamStorageService>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IRecordingOrchestrator _recordingOrchestrator = Substitute.For<IRecordingOrchestrator>();
    private readonly ILogger<StreamFinalizer> _logger = Substitute.For<ILogger<StreamFinalizer>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;

    private readonly Channel _channel = Channel.Create("chan_1", "testchannel", 1, isArchived: false);

    public StreamFinalizerTests()
    {
        _dataStore = new EfDataStore(_factory);
    }

    private StreamFinalizer CreateSut()
    {
        using var db = _factory.CreateDbContext();
        if (!db.Channels.Any(c => c.Id == _channel.Id))
        {
            db.Channels.Add(_channel);
            db.SaveChanges();
        }
        return new(_dataStore, _storageService, _twitchClient, _dateTimeProvider, _logger);
    }

    private static Domain.Stream CreateStream(string twitchStreamId = "ts_1", string channelId = "chan_1", DateTime? startedAt = null)
    {
        return Domain.Stream.Create(
            twitchStreamId,
            channelId,
            StreamFolder.Create("streams_root", "testchannel"),
            startedAt ?? new DateTime(2026, 1, 1, 10, 0, 0),
            "Test Title",
            "Test Category");
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public async Task FinalizeAsync_ShouldAlwaysDelete_RegardlessOfEndReason_WhenMarkedForDeletion(SessionEndReason reason)
    {
        // Arrange
        var stream = CreateStream();
        stream.SetStorageOperationStatus(StorageOperationStatus.DeleteRequest);
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, reason);

        // Assert
        using var dbCheck = _factory.CreateDbContext();
        dbCheck.Channels.First(c => c.Id == _channel.Id).IsLive.Should().BeFalse();
    }


    [Fact]
    public async Task FinalizeAsync_ShouldMarkStopped_WhenReasonIsStreamStopped()
    {
        // Arrange
        var stream = CreateStream();
        var stoppedAt = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(stoppedAt);
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamStopped());

        // Assert
        stream.Status.Should().Be(StreamStatus.Stopped);
        stream.FinishedAt.Should().Be(stoppedAt);
        using var dbCheck = _factory.CreateDbContext();
        dbCheck.Channels.First(c => c.Id == _channel.Id).IsLive.Should().BeFalse();
        await _storageService.Received(1).FinalizeStorageAsync(stream);
    }


    [Fact]
    public async Task FinalizeAsync_ShouldMarkInterrupted_WhenErrorOccursAndChannelStillLiveWithSameStream()
    {
        // Arrange
        var stream = CreateStream("ts_current");
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>())
            .Returns(new StreamMetadata("ts_current", "title", "cat", DateTime.Now));
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("network blip")));

        // Assert
        stream.Status.Should().Be(StreamStatus.Interrupted);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldNotMarkInterrupted_WhenTwitchReportsADifferentStreamId()
    {
        // Arrange
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>())
            .Returns(new StreamMetadata("ts_new", "title", "cat", DateTime.Now));

        var stream = CreateStream("ts_old");
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("fatal")));

        // Assert
        stream.Status.Should().NotBe(StreamStatus.Interrupted);
    }


    [Fact]
    public async Task FinalizeAsync_ShouldMarkFinished_WhenReasonIsStreamEnded()
    {
        // Arrange
        var stream = CreateStream(startedAt: new DateTime(2026, 1, 1, 10, 0, 0));
        var finishedAt = new DateTime(2026, 1, 1, 11, 30, 0);
        _dateTimeProvider.DateTimeNow.Returns(finishedAt);
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamEnded());

        // Assert
        stream.Status.Should().Be(StreamStatus.Finished);
        stream.FinishedAt.Should().Be(finishedAt);
        using var dbCheck = _factory.CreateDbContext();
        dbCheck.Channels.First(c => c.Id == _channel.Id).IsLive.Should().BeFalse();
        await _storageService.Received(1).FinalizeStorageAsync(stream);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldSwallowException_WhenTwitchClientThrowsDuringErrorHandling()
    {
        // Arrange
        var stream = CreateStream();
        _twitchClient.GetStreamMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamMetadata?>(new Exception("twitch api down")));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.FinalizeAsync(
            _channel,
            stream,
            sizeBytes: 0,
            new SessionEndReason.StreamError(new Exception("original error")));

        // Assert
        await act.Should().NotThrowAsync();
    }

    public static TheoryData<SessionEndReason> AllReasons() => new()
    {
        new SessionEndReason.StreamStopped(),
        new SessionEndReason.StreamEnded(),
        new SessionEndReason.StreamError(new InvalidOperationException("boom"))
    };
}