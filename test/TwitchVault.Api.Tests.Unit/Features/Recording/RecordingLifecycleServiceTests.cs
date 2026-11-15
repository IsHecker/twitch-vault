using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

public class RecordingLifecycleServiceTests
{
    private readonly IRecordingOrchestrator _orchestrator = Substitute.For<IRecordingOrchestrator>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IStreamFinalizer _streamFinalizer = Substitute.For<IStreamFinalizer>();
    private readonly ILogger<RecordingLifecycleService> _logger = Substitute.For<ILogger<RecordingLifecycleService>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;

    public RecordingLifecycleServiceTests()
    {
        _dataStore = new EfDataStore(_factory);
    }

    private RecordingLifecycleService CreateSut() =>
        new(_orchestrator, _dataStore, _twitchGqlClient, _streamFinalizer, _logger);

    private Channel SeedChannel(string id, string name, bool isLive = false, bool isArchived = false)
    {
        using var db = _factory.CreateDbContext();
        var channel = Channel.Create(id, name, 1, isArchived);
        channel.SetLive(isLive);
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private Api.Features.Streams.Stream SeedStream(string id, string channelId, StreamStatus status, DateTime? finishedAt = null)
    {
        using var db = _factory.CreateDbContext();
        var stream = Api.Features.Streams.Stream.Create(
            id,
            channelId,
            StreamFolder.Create("streams_root", "testchannel", id),
            DateTime.UtcNow,
            "Title",
            "Category");

        if (status == StreamStatus.Interrupted)
            stream.MarkAsInterrupted();
        else if (status == StreamStatus.Finished && finishedAt.HasValue)
            stream.MarkAsFinished(finishedAt.Value);
        else if (status == StreamStatus.Finalizing)
            stream.MarkAsFinalizing(finishedAt ?? DateTime.UtcNow);
        else if (status == StreamStatus.Stopped)
            stream.MarkAsStopped(finishedAt ?? DateTime.UtcNow);

        db.Streams.Add(stream);
        db.SaveChanges();
        return stream;
    }

    [Fact]
    public async Task StartAsync_ShouldIgnoreStreams_WhenStatusIsNotRecordingAndStopped()
    {
        // Arrange
        var channel = SeedChannel("chan_1", "offline_channel", isLive: false);
        SeedStream("stream_finalizing", channel.Id, StreamStatus.Finalizing);
        SeedStream("stream_finished", channel.Id, StreamStatus.Finished, DateTime.Today);
        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        await _twitchGqlClient.DidNotReceive().IsChannelLiveAsync(Arg.Any<List<Channel>>(), Arg.Any<CancellationToken>());
        await _streamFinalizer.DidNotReceive().FinalizeAsync(
            Arg.Any<Channel>(),
            Arg.Any<Api.Features.Streams.Stream>(),
            Arg.Any<long>(),
            Arg.Any<SessionEndReason>());
    }

    [Fact]
    public async Task StartAsync_ShouldDoNothing_WhenNoLiveOrInterruptedChannelsExist()
    {
        // Arrange
        SeedChannel("chan_1", "offline_channel", isLive: false);
        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        await _twitchGqlClient.DidNotReceive().IsChannelLiveAsync(Arg.Any<List<Channel>>(), Arg.Any<CancellationToken>());
        await _orchestrator.DidNotReceive().TryStartRecordingAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StartAsync_ShouldResumeRecording_WhenMonitoredLiveChannelIsLiveOnTwitch()
    {
        // Arrange
        var channel = SeedChannel("chan_1", "live_channel", isLive: true);
        SeedStream("stream_1", channel.Id, StreamStatus.Interrupted, finishedAt: null);
        _twitchGqlClient.IsChannelLiveAsync(Arg.Any<List<Channel>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var channels = args.Arg<List<Channel>>();
                return channels.ToDictionary(c => c, _ => true);
            });

        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        await _orchestrator.Received(1).TryStartRecordingAsync(channel.Id, channel.Name);
    }

    [Fact]
    public async Task StartAsync_ShouldResumeRecording_WhenInterruptedStreamExistsEvenIfChannelNotMarkedLiveInDb()
    {
        // Arrange
        var channel = SeedChannel("chan_1", "interrupted_channel", isLive: false);
        SeedStream("stream_1", channel.Id, StreamStatus.Interrupted, finishedAt: null);

        _twitchGqlClient.IsChannelLiveAsync(Arg.Any<List<Channel>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var channels = args.Arg<List<Channel>>();
                return channels.ToDictionary(c => c, _ => true);
            });

        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        await _orchestrator.Received(1).TryStartRecordingAsync(channel.Id, channel.Name);
    }

    [Fact]
    public async Task StartAsync_ShouldResetStaleChannelAndStreams_WhenCandidateIsOfflineOnTwitch()
    {
        // Arrange
        var channel = SeedChannel("chan_1", "offline_candidate", isLive: true);
        SeedStream("stream_1", channel.Id, StreamStatus.Interrupted, finishedAt: null);
        _twitchGqlClient.IsChannelLiveAsync(Arg.Any<List<Channel>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var channels = args.Arg<List<Channel>>();
                return channels.ToDictionary(c => c, _ => false);
            });

        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        await _orchestrator.DidNotReceive().TryStartRecordingAsync(Arg.Any<string>(), Arg.Any<string>());

        await _streamFinalizer.Received(1).FinalizeAsync(
            Arg.Is<Channel>(c => c.Id == channel.Id),
            Arg.Is<Api.Features.Streams.Stream>(s => s.Id == "stream_1"),
            Arg.Any<long>(),
            Arg.Any<SessionEndReason.StreamEnded>());
    }

    [Fact]
    public async Task StopAsync_ShouldCallShutdownAllRecordingsAsync()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.StopAsync(CancellationToken.None);

        // Assert
        await _orchestrator.Received(1).StopAllRecordingsAsync();
    }
}