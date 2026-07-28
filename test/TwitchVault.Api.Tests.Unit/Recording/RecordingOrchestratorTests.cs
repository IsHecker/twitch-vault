using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class RecordingOrchestratorTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";

    private readonly IStreamRecorderRegistry _streamRecorderRegistry = Substitute.For<IStreamRecorderRegistry>();
    private readonly IStreamRecorderFactory _streamRecorderFactory = Substitute.For<IStreamRecorderFactory>();
    private readonly IChannelRepository _channelRepository = Substitute.For<IChannelRepository>();
    private readonly IStreamRepository _streamRepository = Substitute.For<IStreamRepository>();
    private readonly IStreamService _streamService = Substitute.For<IStreamService>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ILogger<RecordingOrchestrator> _logger = Substitute.For<ILogger<RecordingOrchestrator>>();

    private readonly CancellationTokenSource _appStoppingCts = new();
    private readonly IHostApplicationLifetime _appLifetime = Substitute.For<IHostApplicationLifetime>();

    public RecordingOrchestratorTests()
    {
        _appLifetime.ApplicationStopping.Returns(_appStoppingCts.Token);

        // Constructor fires-and-forgets ResetStaleChannelsAsync, which calls this - default to empty
        // so it's a no-op unless a test overrides it.
        _channelRepository.GetAllAsync().Returns([]);
    }

    private RecordingOrchestrator CreateSut() =>
        new(_streamRecorderRegistry,
            _streamRecorderFactory,
            _channelRepository,
            _streamRepository,
            _streamService,
            _twitchGqlClient,
            _logger,
            _appLifetime);

    private static Channel CreateChannel(string id = ChannelId, string name = ChannelName) =>
        new() { Id = id, Name = name, QualityRank = 1 };

    private static StreamMetadata CreateMetadata(string twitchStreamId = "ts_1") =>
        new(twitchStreamId, "Some Title", "Some Game", DateTime.Now);

    private static Domain.Stream CreateStream(string twitchStreamId, string channelId) =>
        new() { TwitchStreamId = twitchStreamId, ChannelId = channelId };

    private IStreamRecorder StubFactoryReturnsRecorder()
    {
        var recorder = Substitute.For<IStreamRecorder>();

        _streamRecorderFactory
            .CreateAsync(Arg.Any<Domain.Stream>(), Arg.Any<Channel>(), Arg.Any<CancellationToken>())
            .Returns(recorder);

        return recorder;
    }


    [Fact]
    public async Task Constructor_ShouldResetLiveChannels_OnStartup()
    {
        // Arrange
        var liveChannel = CreateChannel();
        liveChannel.IsLive = true;
        var offlineChannel = CreateChannel("other_id", "other_channel");
        offlineChannel.IsLive = false;

        _channelRepository.GetAllAsync().Returns([liveChannel, offlineChannel]);

        // Act
        _ = CreateSut();
        await Task.Delay(50); // allow fire-and-forget ResetStaleChannelsAsync to run

        // Assert
        await _channelRepository.Received(1).SetLiveAsync(liveChannel.Id, false);
        await _channelRepository.DidNotReceive().SetLiveAsync(offlineChannel.Id, Arg.Any<bool>());
    }


    [Fact]
    public async Task HandleStreamOnlineAsync_ShouldDoNothing_WhenChannelAlreadyBeingRecorded()
    {
        // Arrange
        _streamRecorderRegistry.TryRegister(ChannelId).Returns(false);
        var sut = CreateSut();

        // Act
        await sut.HandleStreamOnlineAsync(ChannelId, ChannelName);

        // Assert
        await _channelRepository.DidNotReceive().GetByIdAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task HandleStreamOnlineAsync_ShouldStartNewStream_WhenChannelExistsAndNoExistingStream()
    {
        // Arrange
        var channel = CreateChannel();
        var metadata = CreateMetadata();
        var createdStream = CreateStream(metadata.TwitchStreamId, channel.Id);
        var recorder = StubFactoryReturnsRecorder();

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _channelRepository.GetByIdAsync(ChannelId).Returns(channel);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);
        _streamRepository.GetStreamsByChannelIdAsync(channel.Id).Returns([]);
        _streamService.CreateAsync(channel, metadata).Returns(createdStream);
        var sut = CreateSut();

        // Act
        await sut.HandleStreamOnlineAsync(ChannelId, ChannelName);

        // Assert
        await _streamService.Received(1).CreateAsync(channel, metadata);
        await _channelRepository.Received(1).SetLiveAsync(channel.Id, true);
        await _channelRepository.Received(1).UpdateLastStreamedAtAsync(channel.Id, createdStream.StartedAt);
        await _streamRecorderFactory.Received(1).CreateAsync(createdStream, channel, Arg.Any<CancellationToken>());
        _streamRecorderRegistry.Received(1).Register(channel.Id, recorder, Arg.Any<Task>());
    }

    [Fact]
    public async Task HandleStreamOnlineAsync_ShouldSwallowAndLogException_WhenUnhandledExceptionOccurs()
    {
        // Arrange
        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _channelRepository.GetByIdAsync(ChannelId).Returns(Task.FromException<Channel?>(new InvalidOperationException("boom")));
        var sut = CreateSut();

        // Act
        var act = async () => await sut.HandleStreamOnlineAsync(ChannelId, ChannelName);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleStreamOnlineAsync_ShouldNotSwallow_OperationCanceledException()
    {
        // Arrange
        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _channelRepository.GetByIdAsync(ChannelId).Returns(Task.FromException<Channel?>(new OperationCanceledException()));
        var sut = CreateSut();

        // Act
        var act = async () => await sut.HandleStreamOnlineAsync(ChannelId, ChannelName);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StartAsync_ShouldStartNewStream_WhenNoStreamMatchesTwitchStreamId()
    {
        // Arrange
        var channel = CreateChannel();
        var metadata = CreateMetadata("ts_new");
        var createdStream = CreateStream("ts_new", channel.Id);

        _streamRepository.GetStreamsByChannelIdAsync(channel.Id).Returns(
        [
            CreateStream("ts_other", channel.Id)
        ]);

        _streamService.CreateAsync(channel, metadata).Returns(createdStream);
        StubFactoryReturnsRecorder();
        var sut = CreateSut();

        // Act
        await sut.StartAsync(channel, metadata);

        // Assert
        await _streamService.Received(1).CreateAsync(channel, metadata);
    }

    [Theory]
    [InlineData(StreamStatus.Recording)]
    [InlineData(StreamStatus.Interrupted)]
    public async Task StartAsync_ShouldResumeStream_WhenExistingStreamIsRecordingOrInterrupted(StreamStatus status)
    {
        // Arrange
        var channel = CreateChannel();
        var metadata = CreateMetadata("ts_existing");
        var existingStream = CreateStream("ts_existing", channel.Id);

        if (status == StreamStatus.Interrupted)
            existingStream.MarkAsInterrupted();

        _streamRepository.GetStreamsByChannelIdAsync(channel.Id).Returns([existingStream]);
        _ = StubFactoryReturnsRecorder();
        var sut = CreateSut();

        // Act
        await sut.StartAsync(channel, metadata);

        // Assert
        existingStream.Status.Should().Be(StreamStatus.Recording);
        await _streamService.DidNotReceive().CreateAsync(Arg.Any<Channel>(), Arg.Any<StreamMetadata>());
        await _streamRecorderFactory.Received(1).CreateAsync(existingStream, channel, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StreamStatus.Finished)]
    [InlineData(StreamStatus.Stopped)]
    public async Task StartAsync_ShouldDoNothing_WhenExistingStreamIsFinishedOrStopped(StreamStatus status)
    {
        // Arrange
        var channel = CreateChannel();
        var metadata = CreateMetadata("ts_existing");
        var existingStream = CreateStream("ts_existing", channel.Id);

        if (status == StreamStatus.Finished)
            existingStream.MarkAsFinished(DateTime.UtcNow);
        else
            existingStream.MarkAsStopped(DateTime.UtcNow);

        _streamRepository.GetStreamsByChannelIdAsync(channel.Id).Returns([existingStream]);
        var sut = CreateSut();

        // Act
        await sut.StartAsync(channel, metadata);

        // Assert
        await _streamService.DidNotReceive().CreateAsync(Arg.Any<Channel>(), Arg.Any<StreamMetadata>());
        await _streamRepository.DidNotReceive().UpdateAsync(Arg.Any<Domain.Stream>());
        await _streamRecorderFactory.DidNotReceive().CreateAsync(Arg.Any<Domain.Stream>(), Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldAlwaysSetChannelLive_RegardlessOfBranch()
    {
        // Arrange
        var channel = CreateChannel();
        var metadata = CreateMetadata("ts_new");
        _streamRepository.GetStreamsByChannelIdAsync(channel.Id).Returns([]);
        _streamService.CreateAsync(channel, metadata).Returns(CreateStream("ts_new", channel.Id));
        StubFactoryReturnsRecorder();
        var sut = CreateSut();

        // Act
        await sut.StartAsync(channel, metadata);

        // Assert
        await _channelRepository.Received(1).SetLiveAsync(channel.Id, true);
    }

    // --- ResumeStreamAsync ---

    [Fact]
    public async Task ResumeStreamAsync_ShouldMarkStreamAsRecordingAndPersist()
    {
        // Arrange
        var channel = CreateChannel();
        var stream = CreateStream("ts_resume", channel.Id);
        stream.MarkAsInterrupted();
        StubFactoryReturnsRecorder();
        var sut = CreateSut();

        // Act
        await sut.ResumeStreamAsync(stream, channel);

        // Assert
        stream.Status.Should().Be(StreamStatus.Recording);
        await _streamRepository.Received().UpdateAsync(stream);
        await _streamRecorderFactory.Received(1).CreateAsync(stream, channel, Arg.Any<CancellationToken>());
    }

    // --- StopRecordingAsync ---

    [Fact]
    public async Task StopRecordingAsync_ShouldCallStopOnSession_WhenSessionExists()
    {
        // Arrange
        var recorder = Substitute.For<IStreamRecorder>();
        _streamRecorderRegistry.TryGet("stream_1", out Arg.Any<IStreamRecorder>())
            .Returns(x =>
            {
                x[1] = recorder;
                return true;
            });
        var sut = CreateSut();

        // Act
        await sut.StopRecordingAsync("stream_1");

        // Assert
        await recorder.Received(1).StopAsync();
    }

    [Fact]
    public async Task StopRecordingAsync_ShouldDoNothing_WhenNoSessionExists()
    {
        // Arrange
        _streamRecorderRegistry.TryGet("missing_stream", out Arg.Any<IStreamRecorder>()).Returns(false);
        var sut = CreateSut();

        // Act
        var act = async () => await sut.StopRecordingAsync("missing_stream");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ToggleStreamDeletionAsync_ShouldCallToggleOnSession_WhenSessionExists()
    {
        // Arrange
        var recorder = StubFactoryReturnsRecorder();
        _streamRecorderRegistry.TryGet("stream_1", out Arg.Any<IStreamRecorder>())
            .Returns(x =>
            {
                x[1] = recorder;
                return true;
            });

        var sut = CreateSut();

        // Act
        await sut.ToggleStreamDeletionAsync("stream_1", true);

        // Assert
        await recorder.Received(1).ToggleStreamDeletionAsync(true);
    }

    [Fact]
    public async Task ToggleStreamDeletionAsync_ShouldDoNothing_WhenNoSessionExists()
    {
        // Arrange
        _streamRecorderRegistry.TryGet("missing_stream", out Arg.Any<IStreamRecorder>()).Returns(false);
        var sut = CreateSut();

        // Act
        var act = async () => await sut.ToggleStreamDeletionAsync("missing_stream", false);

        // Assert
        await act.Should().NotThrowAsync();
    }
}