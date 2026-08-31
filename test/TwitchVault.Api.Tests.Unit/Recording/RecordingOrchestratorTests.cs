using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Persistence.Database;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class RecordingOrchestratorTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";

    private readonly IStreamRecorderRegistry _streamRecorderRegistry = Substitute.For<IStreamRecorderRegistry>();
    private readonly IStreamRecorderFactory _streamRecorderFactory = Substitute.For<IStreamRecorderFactory>();
    private readonly IStreamService _streamService = Substitute.For<IStreamService>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ILogger<RecordingOrchestrator> _logger = Substitute.For<ILogger<RecordingOrchestrator>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;
    private readonly CancellationTokenSource _appStoppingCts = new();
    private readonly IHostApplicationLifetime _appLifetime = Substitute.For<IHostApplicationLifetime>();

    public RecordingOrchestratorTests()
    {
        _dataStore = new EfDataStore(_factory);
        _appLifetime.ApplicationStopping.Returns(_appStoppingCts.Token);
    }

    // The orchestrator now owns its DB access via IDataStore, so the sut
    // just needs the same store wrapping the test factory.
    private RecordingOrchestrator CreateSut() =>
        new(_streamRecorderRegistry,
            _streamRecorderFactory,
            _dataStore,
            _streamService,
            _twitchGqlClient,
            _logger,
            _appLifetime);

    private static Channel CreateChannel(string id = ChannelId, string name = ChannelName) =>
        Channel.Create(id, name, 1, isArchived: false);

    private static StreamMetadata CreateMetadata(string twitchStreamId = "ts_1") =>
        new(twitchStreamId, "Some Title", "Some Game", DateTime.Now);

    private static Domain.Stream CreateStream(string twitchStreamId, string channelId) =>
        Domain.Stream.Create(twitchStreamId, channelId, StreamFolder.Create("streams_root", ChannelName), DateTime.Now, "Test", "Test");

    private IStreamRecorder StubFactoryReturnsRecorder()
    {
        var recorder = Substitute.For<IStreamRecorder>();

        _streamRecorderFactory
            .CreateAsync(Arg.Any<Domain.Stream>(), Arg.Any<Channel>(), Arg.Any<CancellationToken>())
            .Returns(recorder);

        return recorder;
    }

    // --- TryStartRecordingAsync (entry point / dedupe / error handling) ---

    [Fact]
    public async Task TryStartRecordingAsync_ShouldDoNothing_WhenChannelAlreadyBeingRecorded()
    {
        // Arrange — short-circuits before any DbContext is ever created, nothing to seed
        _streamRecorderRegistry.TryRegister(ChannelId).Returns(false);
        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        await _twitchGqlClient.DidNotReceive().GetStreamMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartRecordingAsync_ShouldSwallowAndLogException_WhenUnhandledExceptionOccurs()
    {
        // Arrange — channel exists (so we get past the null-channel branch), gql client throws instead.
        // Seeded via a throwaway context; the orchestrator goes through IDataStore.
        using (var db = _factory.CreateDbContext())
        {
            db.Channels.Add(CreateChannel());
            await db.SaveChangesAsync();
        }

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamMetadata?>(new InvalidOperationException("boom")));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TryStartRecordingAsync_ShouldNotSwallow_OperationCanceledException()
    {
        // Arrange
        using (var db = _factory.CreateDbContext())
        {
            db.Channels.Add(CreateChannel());
            await db.SaveChangesAsync();
        }

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamMetadata?>(new OperationCanceledException()));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- RecordStreamAsync (private — exercised through TryStartRecordingAsync) ---

    [Fact]
    public async Task RecordStreamAsync_ShouldCreateNewStream_WhenChannelExistsAndNoExistingStream()
    {
        // Arrange — seed context is separate from the one the orchestrator will create for itself
        using var db = _factory.CreateDbContext();
        var channel = CreateChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        var metadata = CreateMetadata();
        var createdStream = CreateStream(metadata.Id, channel.Id);
        var recorder = Substitute.For<IStreamRecorder>();

        Channel? capturedChannel = null;
        _streamRecorderFactory
            .CreateAsync(Arg.Any<Domain.Stream>(), Arg.Do<Channel>(c => capturedChannel = c), Arg.Any<CancellationToken>())
            .Returns(recorder);

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);
        // CreateStream is synchronous now, and the orchestrator itself adds the returned stream to its context.
        _streamService.CreateStream(Arg.Is<Channel>(c => c.Id == channel.Id), metadata).Returns(createdStream);

        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        _streamService.Received(1).CreateStream(Arg.Is<Channel>(c => c.Id == channel.Id), metadata);
        await _streamRecorderFactory.Received(1).CreateAsync(createdStream, Arg.Any<Channel>(), Arg.Any<CancellationToken>());
        _streamRecorderRegistry.Received(1).Register(channel.Id, recorder, Arg.Any<Task>());

        // capturedChannel is the orchestrator's own internally-loaded Channel — a different
        // instance than our seed `channel`, but the one that actually got mutated.
        capturedChannel.Should().NotBeNull();
        capturedChannel!.IsLive.Should().BeTrue();
        capturedChannel.LastStreamedAt.Should().Be(createdStream.StartedAt);

        // Verify persistence with a fresh context — the seed `db` never tracked this stream,
        // so it can't tell us what the orchestrator actually saved.
        await using var verifyDb = _factory.CreateDbContext();
        var savedStream = await verifyDb.Streams.GetByIdAsync(createdStream.Id);
        savedStream.Should().NotBeNull();
        savedStream!.Status.Should().Be(StreamStatus.Recording);
    }

    [Fact]
    public async Task RecordStreamAsync_ShouldIgnoreUnrelatedExistingStream_AndCreateNewOne()
    {
        // Arrange
        using var db = _factory.CreateDbContext();
        var channel = CreateChannel();
        db.Channels.Add(channel);
        db.Streams.Add(CreateStream("ts_other", channel.Id));
        await db.SaveChangesAsync();

        var metadata = CreateMetadata("ts_new");
        var createdStream = CreateStream("ts_new", channel.Id);

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);
        _streamService.CreateStream(Arg.Is<Channel>(c => c.Id == channel.Id), metadata).Returns(createdStream);
        StubFactoryReturnsRecorder();

        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        _streamService.Received(1).CreateStream(Arg.Is<Channel>(c => c.Id == channel.Id), metadata);
    }

    [Fact]
    public async Task RecordStreamAsync_ShouldResumeStreamAndPersist_WhenExistingStreamIsInterrupted()
    {
        // Arrange
        using var db = _factory.CreateDbContext();
        var channel = CreateChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        var metadata = CreateMetadata("ts_existing");
        var existingStream = CreateStream("ts_existing", channel.Id);
        existingStream.MarkAsInterrupted();
        db.Streams.Add(existingStream);
        await db.SaveChangesAsync();

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);
        StubFactoryReturnsRecorder();

        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        _streamService.DidNotReceive().CreateStream(Arg.Any<Channel>(), Arg.Any<StreamMetadata>());
        // Match by Id, not by reference — the orchestrator loaded its own Channel instance via its own context.
        await _streamRecorderFactory.Received(1)
            .CreateAsync(Arg.Is<Domain.Stream>(s => s.Id == "ts_existing"), Arg.Is<Channel>(c => c.Id == channel.Id), Arg.Any<CancellationToken>());

        // `db` already tracks the pre-mutation `existingStream` (Interrupted) — re-querying it would
        // just hand back that stale tracked instance. Use a fresh context to see what was really saved.
        await using var verifyDb = _factory.CreateDbContext();
        var persisted = await verifyDb.Streams.GetByIdAsync("ts_existing");
        persisted!.Status.Should().Be(StreamStatus.Recording);
    }

    [Theory]
    [InlineData(StreamStatus.Recording)]
    [InlineData(StreamStatus.Finished)]
    [InlineData(StreamStatus.Stopped)]
    public async Task RecordStreamAsync_ShouldDoNothing_WhenExistingStreamIsNotInterrupted(StreamStatus status)
    {
        // Arrange
        using var db = _factory.CreateDbContext();
        var channel = CreateChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        var metadata = CreateMetadata("ts_existing");
        var existingStream = CreateStream("ts_existing", channel.Id);

        switch (status)
        {
            case StreamStatus.Finished:
                existingStream.MarkAsFinished(DateTime.UtcNow);
                break;
            case StreamStatus.Stopped:
                existingStream.MarkAsStopped(DateTime.UtcNow);
                break;
        }

        db.Streams.Add(existingStream);
        await db.SaveChangesAsync();

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);

        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert
        _streamService.DidNotReceive().CreateStream(Arg.Any<Channel>(), Arg.Any<StreamMetadata>());
        await _streamRecorderFactory.DidNotReceive().CreateAsync(Arg.Any<Domain.Stream>(), Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordStreamAsync_ShouldAlwaysSetChannelLive_RegardlessOfBranch()
    {
        // Arrange
        using var db = _factory.CreateDbContext();
        var channel = CreateChannel();
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        var metadata = CreateMetadata("ts_new");

        Channel? capturedChannel = null;
        _streamRecorderFactory
            .CreateAsync(Arg.Any<Domain.Stream>(), Arg.Do<Channel>(c => capturedChannel = c), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IStreamRecorder>());

        _streamRecorderRegistry.TryRegister(ChannelId).Returns(true);
        _twitchGqlClient.GetStreamMetadataAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(metadata);
        _streamService.CreateStream(Arg.Any<Channel>(), metadata).Returns(CreateStream("ts_new", channel.Id));

        var sut = CreateSut();

        // Act
        await sut.TryStartRecordingAsync(ChannelId, ChannelName);

        // Assert — assert on the orchestrator's own Channel instance (captured off the mock call),
        // not our seed `channel`, since they live in different DbContexts now.
        capturedChannel.Should().NotBeNull();
        capturedChannel!.IsLive.Should().BeTrue();
    }

    // --- StopRecordingAsync (no DbContext involved at all) ---

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
}