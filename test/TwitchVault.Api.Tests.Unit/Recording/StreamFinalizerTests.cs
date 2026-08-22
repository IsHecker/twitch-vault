using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamFinalizerTests
{
    private readonly IStreamRepository _streamRepository = Substitute.For<IStreamRepository>();
    private readonly IChannelRepository _channelRepository = Substitute.For<IChannelRepository>();
    private readonly ITwitchGqlClient _twitchClient = Substitute.For<ITwitchGqlClient>();
    private readonly IStreamStorageService _storageService = Substitute.For<IStreamStorageService>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IRecordingOrchestrator _recordingOrchestrator = Substitute.For<IRecordingOrchestrator>();
    private readonly ILogger<StreamFinalizer> _logger = Substitute.For<ILogger<StreamFinalizer>>();

    private readonly Channel _channel = new() { Id = "chan_1", Name = "testchannel" };

    private StreamFinalizer CreateSut() =>
        new(_streamRepository, _channelRepository, _storageService, _twitchClient, _dateTimeProvider, _recordingOrchestrator, _logger);

    private static Domain.Stream CreateStream(string twitchStreamId = "ts_1", string channelId = "chan_1")
    {
        return new Domain.Stream
        {
            TwitchStreamId = twitchStreamId,
            ChannelId = channelId,
            Folder = StreamFolder.Create("streams_root", "testchannel")
        };
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
        await sut.FinalizeAsync(_channel.Name, stream, sizeBytes: 0, reason);

        // Assert
        await _channelRepository.Received().SetLiveAsync(Arg.Any<string>(), Arg.Any<bool>());
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
        await sut.FinalizeAsync(_channel.Name, stream, sizeBytes: 0, new SessionEndReason.StreamStopped());

        // Assert
        stream.Status.Should().Be(StreamStatus.Stopped);
        stream.FinishedAt.Should().Be(stoppedAt);
        await _channelRepository.Received().SetLiveAsync(Arg.Any<string>(), Arg.Any<bool>());
        await _storageService.Received(1).TryFinalizeStorageAsync(stream);
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
        await sut.FinalizeAsync(_channel.Name, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("network blip")));

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
        await sut.FinalizeAsync(_channel.Name, stream, sizeBytes: 0, new SessionEndReason.StreamError(new Exception("fatal")));

        // Assert
        stream.Status.Should().NotBe(StreamStatus.Interrupted);
    }


    [Fact]
    public async Task FinalizeAsync_ShouldMarkFinished_WhenReasonIsStreamEnded()
    {
        // Arrange
        var stream = CreateStream();
        stream.StartedAt = new DateTime(2026, 1, 1, 10, 0, 0);
        var finishedAt = new DateTime(2026, 1, 1, 11, 30, 0);
        _dateTimeProvider.DateTimeNow.Returns(finishedAt);
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(_channel.Name, stream, sizeBytes: 0, new SessionEndReason.StreamEnded());

        // Assert
        stream.Status.Should().Be(StreamStatus.Finished);
        stream.FinishedAt.Should().Be(finishedAt);
        await _channelRepository.Received(1).SetLiveAsync(_channel.Id, false);
        await _storageService.Received(1).TryFinalizeStorageAsync(stream);
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
            _channel.Name,
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