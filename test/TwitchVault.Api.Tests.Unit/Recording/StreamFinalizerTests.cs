using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamFinalizerTests
{
    private readonly IStreamRepository _streamRepository = Substitute.For<IStreamRepository>();
    private readonly IChannelRepository _channelRepository = Substitute.For<IChannelRepository>();
    private readonly IStreamService _streamService = Substitute.For<IStreamService>();
    private readonly ITwitchGqlClient _twitchClient = Substitute.For<ITwitchGqlClient>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly ILogger<StreamFinalizer> _logger = Substitute.For<ILogger<StreamFinalizer>>();

    private readonly ISegmentDownloader _segmentDownloader = Substitute.For<ISegmentDownloader>();

    private readonly Channel _channel = new() { Id = "chan_1", Name = "testchannel" };

    public StreamFinalizerTests()
    {
        _pathsOptions.Value.Returns(new PathsOptions { BaseUrl = "https://cdn.example.com" });
    }

    private StreamFinalizer CreateSut() =>
        new(_streamRepository, _channelRepository, _streamService, _twitchClient, _dateTimeProvider, _pathsOptions, _logger);

    private static Domain.Stream CreateStream(string twitchStreamId = "ts_1", string channelId = "chan_1")
    {
        return new Domain.Stream
        {
            TwitchStreamId = twitchStreamId,
            ChannelId = channelId,
            Folder = StreamFolder.Create("streams_root", "testchannel")
        };
    }

    [Fact]
    public async Task FinalizeAsync_ShouldCloseSegment()
    {
        // Arrange
        var stream = CreateStream();
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamEnded());

        // Assert
        _segmentDownloader.Received(1).CloseSegment();
    }

    [Fact]
    public async Task FinalizeAsync_ShouldPersistThumbnailUrl_UsingConfiguredBaseUrl()
    {
        // Arrange
        var stream = CreateStream();
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamEnded());

        // Assert
        stream.ThumbnailUrl.Should().StartWith("https://cdn.example.com");
        await _streamRepository.Received().UpdateAsync(stream);
    }


    [Theory]
    [MemberData(nameof(AllReasons))]
    public async Task FinalizeAsync_ShouldAlwaysDelete_RegardlessOfEndReason_WhenMarkedForDeletion(SessionEndReason reason)
    {
        // Arrange
        var stream = CreateStream();
        stream.MarkForDeletion = true;
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, reason);

        // Assert
        await _streamService.Received(1).DeleteStreamAsync(stream.TwitchStreamId);
        await _channelRepository.DidNotReceive().SetLiveAsync(Arg.Any<string>(), Arg.Any<bool>());
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
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamStopped());

        // Assert
        stream.Status.Should().Be(StreamStatus.Stopped);
        stream.FinishedAt.Should().Be(stoppedAt);
        await _channelRepository.DidNotReceive().SetLiveAsync(Arg.Any<string>(), Arg.Any<bool>());
    }


    [Fact]
    public async Task FinalizeAsync_ShouldMarkInterrupted_WhenErrorOccursAndChannelStillLiveWithSameStream()
    {
        // Arrange
        var stream = CreateStream("ts_current");
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>())
            .Returns(new StreamMetadata("ts_current", "title", "cat"));
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamError(new Exception("network blip")));

        // Assert
        stream.Status.Should().Be(StreamStatus.Interrupted);
    }


    [Fact]
    public async Task FinalizeAsync_ShouldNotChangeStatus_WhenErrorOccurs_AndChannelNoLongerLive()
    {
        // Arrange
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>()).ReturnsNull();
        var stream = CreateStream("ts_current");
        var originalStatus = stream.Status;
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamError(new Exception("fatal")));

        // Assert
        stream.Status.Should().Be(originalStatus);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldNotMarkInterrupted_WhenTwitchReportsADifferentStreamId()
    {
        // Arrange
        _twitchClient.GetStreamMetadataAsync(_channel.Name, Arg.Any<CancellationToken>())
            .Returns(new StreamMetadata("ts_new", "title", "cat"));

        var stream = CreateStream("ts_old");
        var sut = CreateSut();

        // Act
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamError(new Exception("fatal")));

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
        await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamEnded());

        // Assert
        stream.Status.Should().Be(StreamStatus.Finished);
        stream.FinishedAt.Should().Be(finishedAt);
        await _channelRepository.Received(1).SetLiveAsync(_channel.Id, false);
    }

    [Fact]
    public async Task FinalizeAsync_ShouldSwallowException_WhenDeleteStreamThrows()
    {
        // Arrange
        var stream = CreateStream();
        stream.MarkForDeletion = true;
        _streamService.DeleteStreamAsync(stream.TwitchStreamId)
            .Returns(Task.FromException(new Exception("cannot delete")));
        var sut = CreateSut();

        // Act
        var act = async () => await sut.FinalizeAsync(stream, _channel, _segmentDownloader, new SessionEndReason.StreamEnded());

        // Assert
        await act.Should().NotThrowAsync();
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
            stream,
            _channel,
            _segmentDownloader,
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