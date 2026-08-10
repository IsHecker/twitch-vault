using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using FluentAssertions;
using TwitchVault.Api.Common;
using NSubstitute.ReturnsExtensions;
using DomainStream = TwitchVault.Api.Domain.Stream;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamServiceTests
{
    private readonly StreamService _sut;
    private readonly IStreamRepository _streamRepository = Substitute.For<IStreamRepository>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly ILogger<StreamService> _logger = Substitute.For<ILogger<StreamService>>();

    public StreamServiceTests()
    {
        _pathsOptions.Value.Returns(new PathsOptions { Streams = "Streams" });
        _sut = new StreamService(_streamRepository, _dateTimeProvider, _pathsOptions, _logger);
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldNotDelete_WhenStreamDoesNotExist()
    {
        // Arrange
        _streamRepository.GetByIdAsync(Arg.Any<string>()).ReturnsNull();

        // Act
        await _sut.DeleteStreamAsync("non-existent");

        // Assert
        await _streamRepository.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DeleteStreamAsync_ShouldSucceed_WhenChaptersIsEmpty()
    {
        // Arrange
        var streamId = "stream-123";
        var stream = new DomainStream
        {
            TwitchStreamId = streamId,
            ChannelId = "channel-1",
            Folder = StreamFolder.Create("Streams", "testchannel"),
            Chapters = []
        };

        _streamRepository.GetByIdAsync(streamId).Returns(stream);
        stream.MarkAsFinished(DateTime.UtcNow);

        // Act
        var act = () => _sut.DeleteStreamAsync(streamId);

        // Assert
        await act.Should().NotThrowAsync();
        await _streamRepository.Received(1).DeleteAsync(streamId);
    }

    [Fact]
    public async Task ResetStaleStreamsAsync_ShouldUpdateStaleStreams_WhenStaleStreamsExist()
    {
        // Arrange
        var channelId = "channel-1";
        var staleStream = new DomainStream
        {
            TwitchStreamId = "stale-1",
            ChannelId = channelId
        };

        var activeStreamId = "active-1";
        var activeStream = new DomainStream
        {
            TwitchStreamId = activeStreamId,
            ChannelId = channelId
        };

        _streamRepository.ListByChannelIdAsync(channelId)
            .Returns([staleStream, activeStream]);

        // Act
        await _sut.ResetStaleStreamsAsync(channelId, activeStreamId);

        // Assert
        staleStream.Status.Should().Be(StreamStatus.Finished);
        staleStream.FinishedAt.Should().NotBeNull();

        await _streamRepository.Received(1).UpdateAsync(staleStream);
        await _streamRepository.DidNotReceive().UpdateAsync(activeStream);
    }
}