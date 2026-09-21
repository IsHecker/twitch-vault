using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

public class ThumbnailManagerTests
{
    private const string ChannelName = "testchannel";
    private readonly TwitchVault.Api.Features.Streams.Stream _stream;
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly ILogger<ThumbnailManager> _logger = Substitute.For<ILogger<ThumbnailManager>>();

    public ThumbnailManagerTests()
    {
        _stream = TwitchVault.Api.Features.Streams.Stream.Create(
            "test-stream",
            "channel-1",
            StreamFolder.Create("streams_root", "Test Channel"),
            DateTime.Now,
            "initialTitle",
            "initialCategoryId");

        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ResponseStream(
                new MemoryStream("dummy-image-bytes"u8.ToArray()),
                new HttpResponseMessage()));

        _fileSystem.OpenWrite(Arg.Any<string>(), Arg.Any<FileMode>())
            .Returns(_ => new MemoryStream());
    }

    private ThumbnailManager CreateSut() =>
        new(_twitchGqlClient, _dateTimeProvider, _fileSystem, _logger);

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldCapture_WhenCooldownHasPassed()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = CreateSut();

        // Act
        await sut.TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);
        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(5));
        await sut.TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);

        // Assert
        await _twitchGqlClient.Received(2).DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldNotCapture_WhenWithinCooldown()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = CreateSut();

        // Act
        await sut.TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);
        _twitchGqlClient.ClearReceivedCalls();

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(2));
        await sut.TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);

        // Assert
        await _twitchGqlClient.DidNotReceive().DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryCaptureSnapshotAsync_ShouldStopCapturing_WhenOutside30MinWindow()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);

        var sut = CreateSut();

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddMinutes(30));

        // Act
        await sut.TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);

        // Assert
        await _twitchGqlClient.DidNotReceive().DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}