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
    private readonly ILogger<StreamFinalizer> _logger = Substitute.For<ILogger<StreamFinalizer>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;

    private readonly Channel _channel = Channel.Create("chan_1", "testchannel", 1, isArchived: false);

    public StreamFinalizerTests()
    {
        _dataStore = new EfDataStore(_factory);
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public async Task FinalizeAsync_ShouldSetChannelOffline_ForEveryEndReason(SessionEndReason reason)
    {
        // Arrange
        var sut = CreateSut();
        var stream = SeedStream(CreateStream());

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, reason);

        // Assert
        AssertChannelIsOffline();
    }

    [Fact]
    public async Task FinalizeAsync_ShouldMarkStopped_WhenReasonIsStreamStopped()
    {
        // Arrange
        var sut = CreateSut();
        var stream = SeedStream(CreateStream());
        var stoppedAt = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(stoppedAt);

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamStopped());

        // Assert
        stream.Status.Should().Be(StreamStatus.Stopped);
        stream.FinishedAt.Should().Be(stoppedAt);
        await _storageService.Received(1).FinalizeStorageAsync(stream);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldMarkInterrupted_WhenErrorOccursAndChannelStillLiveWithSameStream()
    {
        // Arrange
        var sut = CreateSut();
        var stream = SeedStream(CreateStream("ts_current"));
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>())
            .Returns(new StreamMetadata("ts_current", "title", "cat", DateTime.Now));

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("network blip")));

        // Assert
        stream.Status.Should().Be(StreamStatus.Interrupted);
        await _storageService.DidNotReceive().FinalizeStorageAsync(Arg.Any<Domain.Stream>());
    }

    [Theory]
    [MemberData(nameof(NotCurrentlyLiveMetadata))]
    public async Task FinalizeAsync_ShouldMarkFinished_WhenChannelIsNotLiveWithSameStreamAfterError(StreamMetadata? metadata)
    {
        // Arrange
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>()).Returns(metadata);

        var sut = CreateSut();
        var stream = SeedStream(CreateStream("ts_old"));
        var finishedAt = new DateTime(2026, 1, 1, 11, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(finishedAt);

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("fatal")));

        // Assert
        stream.Status.Should().Be(StreamStatus.Finished);
        stream.FinishedAt.Should().Be(finishedAt);
        await _storageService.Received(1).FinalizeStorageAsync(stream);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldMarkFinished_WhenReasonIsStreamEnded()
    {
        // Arrange
        var sut = CreateSut();
        var stream = SeedStream(CreateStream(startedAt: new DateTime(2026, 1, 1, 10, 0, 0)));
        var finishedAt = new DateTime(2026, 1, 1, 11, 30, 0);
        _dateTimeProvider.DateTimeNow.Returns(finishedAt);

        // Act
        await sut.FinalizeAsync(_channel, stream, sizeBytes: 0, new SessionEndReason.StreamEnded());

        // Assert
        stream.Status.Should().Be(StreamStatus.Finished);
        stream.FinishedAt.Should().Be(finishedAt);
        await _storageService.Received(1).FinalizeStorageAsync(stream);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldSwallowExceptionAndSkipStorage_WhenTwitchClientThrowsDuringErrorHandling()
    {
        // Arrange
        var sut = CreateSut();
        var stream = SeedStream(CreateStream());
        _twitchClient.GetStreamMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamMetadata?>(new Exception("twitch api down")));

        // Act
        var act = async () => await sut.FinalizeAsync(
            _channel,
            stream,
            sizeBytes: 0,
            new SessionEndReason.StreamError(new Exception("original error")));

        // Assert
        await act.Should().NotThrowAsync();
        AssertChannelIsOffline();
        await _storageService.DidNotReceive().FinalizeStorageAsync(Arg.Any<Domain.Stream>());
    }

    public static TheoryData<SessionEndReason> AllReasons() => new()
    {
        new SessionEndReason.StreamStopped(),
        new SessionEndReason.StreamEnded(),
        new SessionEndReason.StreamError(new InvalidOperationException("boom")),
    };

    public static TheoryData<StreamMetadata?> NotCurrentlyLiveMetadata() => new()
    {
        null,
        new StreamMetadata("ts_new", "title", "cat", DateTime.Now),
    };

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

    private static Domain.Stream CreateStream(string twitchStreamId = "ts_1", string channelId = "chan_1", DateTime? startedAt = null) =>
        Domain.Stream.Create(
            twitchStreamId,
            channelId,
            StreamFolder.Create("streams_root", "testchannel"),
            startedAt ?? new DateTime(2026, 1, 1, 10, 0, 0),
            "Test Title",
            "Test Category");

    private Domain.Stream SeedStream(Domain.Stream stream)
    {
        using var db = _factory.CreateDbContext();
        db.Streams.Add(stream);
        db.SaveChanges();
        return stream;
    }

    private void AssertChannelIsOffline()
    {
        using var db = _factory.CreateDbContext();
        db.Channels.First(c => c.Id == _channel.Id).IsLive.Should().BeFalse();
    }
}