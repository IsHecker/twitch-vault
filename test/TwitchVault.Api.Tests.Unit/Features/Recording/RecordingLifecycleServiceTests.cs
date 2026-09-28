using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

public class RecordingLifecycleServiceTests
{
    private readonly IRecordingOrchestrator _orchestrator = Substitute.For<IRecordingOrchestrator>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ILogger<RecordingLifecycleService> _logger = Substitute.For<ILogger<RecordingLifecycleService>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;

    public RecordingLifecycleServiceTests()
    {
        _dataStore = new EfDataStore(_factory);
    }

    private RecordingLifecycleService CreateSut() =>
        new(_orchestrator, _dataStore, _twitchGqlClient, _logger);

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
            StreamFolder.Create("streams_root", "testchannel"),
            DateTime.UtcNow,
            "Title",
            "Category");

        if (status == StreamStatus.Interrupted)
            stream.MarkAsInterrupted();
        else if (status == StreamStatus.Finished && finishedAt.HasValue)
            stream.MarkAsFinished(finishedAt.Value);

        db.Streams.Add(stream);
        db.SaveChanges();
        return stream;
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

        using var db = _factory.CreateDbContext();
        var refreshedChannel = db.Channels.First(c => c.Id == channel.Id);
        refreshedChannel.IsLive.Should().BeFalse();
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