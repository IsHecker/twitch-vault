using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class ThumbnailManagerTests
{
    private readonly Domain.Stream _stream;
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ILogger<ThumbnailManager> _logger = Substitute.For<ILogger<ThumbnailManager>>();

    public ThumbnailManagerTests()
    {
        _stream = new Domain.Stream
        {
            ChannelId = "channel-1",
            TwitchStreamId = "test-stream",
            ThumbnailUrl = "https://twitch.tv/thumb.jpg"
        };
    }

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldCapture_WhenCooldownHasPassed()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = new ThumbnailManager(_stream, _dateTimeProvider, _twitchGqlClient, _logger);

        var dummyStream = new MemoryStream("dummy-data"u8.ToArray());
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(dummyStream);

        // Act
        await sut.TryCaptureSnapshotAsync();
        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(5));
        await sut.TryCaptureSnapshotAsync();

        // Assert
        await _twitchGqlClient.Received(2).DownloadAsStreamAsync(_stream.ThumbnailUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldNotCapture_WhenWithinCooldown()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = new ThumbnailManager(_stream, _dateTimeProvider, _twitchGqlClient, _logger);

        var dummyStream = new MemoryStream("dummy-data"u8.ToArray());
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(dummyStream);

        // Act
        await sut.TryCaptureSnapshotAsync();
        _twitchGqlClient.ClearReceivedCalls();

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(2));
        await sut.TryCaptureSnapshotAsync();

        // Assert
        await _twitchGqlClient.DidNotReceive().DownloadAsStreamAsync(_stream.ThumbnailUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldStopCapturing_WhenOutside30MinWindow()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = new ThumbnailManager(_stream, _dateTimeProvider, _twitchGqlClient, _logger);

        var dummyStream = new MemoryStream("dummy-data"u8.ToArray());
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(dummyStream);

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(30));

        // Act
        await sut.TryCaptureSnapshotAsync();

        // Assert
        await _twitchGqlClient.DidNotReceive().DownloadAsStreamAsync(_stream.ThumbnailUrl, Arg.Any<CancellationToken>());
    }
}