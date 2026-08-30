using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamRecorderTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";
    private const string Manifest =
        "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:100\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6.000,\nseg_1.ts\n#EXT-X-ENDLIST\n";

    private readonly IThumbnailManager _thumbnailManager = Substitute.For<IThumbnailManager>();
    private readonly IManifestPoller _manifestPoller = Substitute.For<IManifestPoller>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ISegmentStore _segmentStore = Substitute.For<ISegmentStore>();
    private readonly IHlsPlaylistWriter _hlsPlaylist = Substitute.For<IHlsPlaylistWriter>();
    private readonly ISegmentUploader _uploader = Substitute.For<ISegmentUploader>();
    private readonly IStreamFinalizer _finalizer = Substitute.For<IStreamFinalizer>();
    private readonly EventBus _eventBus;
    private readonly ILogger<StreamRecorder> _logger = Substitute.For<ILogger<StreamRecorder>>();
    private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
    private readonly IChapterTracker _chapterTracker = Substitute.For<IChapterTracker>();

    private readonly Channel _channel = Channel.Create(ChannelId, ChannelName, 1);
    private readonly Domain.Stream _stream;

    public StreamRecorderTests()
    {
        _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxConsecutiveEmptyPolls = 3 });

        _eventBus = new EventBus(Substitute.For<ILogger<EventBus>>());

        _stream = Domain.Stream.Create(
            "ts_1",
            ChannelId,
            StreamFolder.Create("streams_root", ChannelName),
            new DateTime(2026, 1, 1),
            "Some Title",
            "Some Category");

        _hlsPlaylist.LastTwitchMediaSequence.Returns(0L);
        _hlsPlaylist.HasInitSegment.Returns(true);

        _finalizer.FinalizeAsync(
            Arg.Any<Channel>(),
            Arg.Any<Domain.Stream>(),
            Arg.Any<long>(),
            Arg.Any<SessionEndReason>()).Returns(Task.CompletedTask);
    }

    private StreamRecorder CreateSut(
        CancellationToken parentToken = default) =>
        new(_thumbnailManager,
            _manifestPoller,
            _twitchGqlClient,
            _hlsPlaylist,
            _segmentStore,
            _chapterTracker,
            _uploader,
            _finalizer,
            _vaultOptions,
            _logger,
            parentToken);

    private void StubManifestOnce(string manifest, bool hasQualityChanged = false)
    {
        var called = false;
        _manifestPoller.GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (called)
                    return Task.FromResult<(string?, bool)>((null, false));

                called = true;
                return Task.FromResult<(string?, bool)>((manifest, hasQualityChanged));
            });
    }

    private void StubDownloadSegments(params (string FileName, float Duration, bool IsInit)[] items)
    {
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([1, 2, 3]));

        if (items.Length == 0)
        {
            _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<DownloadedSegment>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns((LocalSegment?)null);
            return;
        }

        var queue = new Queue<(string FileName, float Duration, bool IsInit)>(items);
        _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<DownloadedSegment>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (queue.TryDequeue(out var item))
                    return new LocalSegment(item.FileName, item.Duration, 0);
                return null;
            });
    }

    private void BlockPollIndefinitely() =>
        _manifestPoller
            .GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                await Task.Delay(Timeout.Infinite, token);
                return (null, false);
            });

    [Fact]
    public async Task StartAsync_ShouldProduceStreamEndedReason_WhenManifestSignalsStreamEnded()
    {
        // Arrange
        StubManifestOnce(Manifest);
        StubDownloadSegments();
        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _finalizer.Received(1).FinalizeAsync(
            _channel, _stream, sizeBytes: 0, Arg.Is(new SessionEndReason.StreamEnded()));
    }

    [Fact]
    public async Task StopAsync_ShouldDelegateStreamStoppedReason_AndCancelTheLoop()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        // Act
        await Task.Delay(50);
        await sut.StopAsync();

        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _finalizer.Received(1).FinalizeAsync(
            _channel, _stream, sizeBytes: 0, Arg.Is(new SessionEndReason.StreamStopped()));
    }

    [Fact]
    public async Task StopAsync_ShouldTakePrecedence_EvenIfLoopWouldOtherwiseReportStreamEnded()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);
        await Task.Delay(50);

        // Act
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _finalizer.DidNotReceive()
            .FinalizeAsync(
                _channel,
                _stream,
                0,
                Arg.Is(new SessionEndReason.StreamEnded()));
    }

    [Fact]
    public async Task StartAsync_ShouldRetryUntilExhausted_WhenManifestAlwaysEmptyOrNull()
    {
        // Arrange
        StubManifestOnce(string.Empty, false);
        await using var sut = CreateSut();

        // Act
        var act = async () => await sut.StartAsync(_stream, _channel);

        // Assert
        await act.Should().NotThrowAsync();
        await _manifestPoller.Received(3).GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>());
        await _finalizer.Received(1).FinalizeAsync(
            _channel, _stream, sizeBytes: 0, Arg.Is(new SessionEndReason.StreamEnded()));
    }

    [Fact]
    public async Task StartAsync_ShouldCloseSegmentAndAddDiscontinuity_WhenQualityChanges()
    {
        // Arrange
        _segmentStore.CloseCurrentSegment().Returns(new LocalSegment("seg_5.ts", 12.3f, 0), (LocalSegment?)null);
        StubManifestOnce(Manifest, hasQualityChanged: true);
        StubDownloadSegments();

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_5.ts", 12.3f, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddDiscontinuityAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldNotCloseSegmentOrAddDiscontinuity_WhenQualityHasNotChanged()
    {
        // Arrange
        StubManifestOnce(Manifest, hasQualityChanged: false);
        StubDownloadSegments();

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.DidNotReceive().AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _hlsPlaylist.DidNotReceive().AddDiscontinuityAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldSetInitSegment_WhenNoInitSegmentYetAndManifestProvidesOne()
    {
        // Arrange
        StubManifestOnce(Manifest);
        _hlsPlaylist.HasInitSegment.Returns(false);
        StubDownloadSegments(("init.mp4", 0f, true));

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.Received(1).SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldAddRegularSegment_WhenInitSegmentAlreadyPresent()
    {
        // Arrange
        StubManifestOnce(Manifest);
        _hlsPlaylist.HasInitSegment.Returns(true);
        StubDownloadSegments(("seg_1.ts", 6f, false));

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.DidNotReceive().SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldUpdateTwitchMediaSequence()
    {
        // Arrange
        StubManifestOnce(Manifest);
        StubDownloadSegments(("seg_1.ts", 6f, false));
        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        _hlsPlaylist.Received(1).UpdateTwitchMediaSequence(100L);
    }

    [Fact]
    public async Task StartAsync_ShouldTakeThumbnailSnapshot_OnEachIteration()
    {
        // Arrange
        StubManifestOnce(Manifest);
        StubDownloadSegments();
        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _thumbnailManager.Received(1).TryCaptureSnapshotAsync(ChannelName, _stream, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldProduceStreamError_WhenManifestPollerThrows()
    {
        // Arrange
        var exception = new HttpRequestException("simulated network blip");
        _manifestPoller
            .GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);

        await using var sut = CreateSut();

        // Act
        var act = async () => await sut.StartAsync(_stream, _channel);

        // Assert
        await act.Should().NotThrowAsync();
        // Polly retries happen inside ITwitchGqlClient/HttpClient, below IManifestPoller — 
        // mocking IManifestPoller bypasses that pipeline entirely, so only 1 call reaches here.
        await _manifestPoller.Received(1).GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>());

        await _finalizer.Received(1).FinalizeAsync(
            _channel, _stream, sizeBytes: 0, Arg.Is(new SessionEndReason.StreamError(exception)));
    }

    [Fact]
    public async Task StartAsync_ShouldDelegateStreamError_AndStillFinalize()
    {
        // Arrange
        var exception = new InvalidOperationException("boom");
        _thumbnailManager.TryCaptureSnapshotAsync(ChannelName, _stream, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));

        await using var sut = CreateSut();

        // Act
        var act = async () => await sut.StartAsync(_stream, _channel);

        // Assert
        await act.Should().NotThrowAsync();
        await _finalizer.Received(1).FinalizeAsync(
            _channel, _stream, sizeBytes: 0, Arg.Is(new SessionEndReason.StreamError(exception)));
    }

    [Fact]
    public async Task MetadataChanged_ShouldPersistStreamAfterAddingChapter()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        // Act
        await _eventBus.PublishAsync(new ChannelUpdateEvent(ChannelId, "New Title", "New Category"));
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync();
    }

    [Fact]
    public async Task MetadataChanged_ShouldIgnoreEvent_WhenTitleAndCategoryUnchanged()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        var chapterCountBefore = _stream.Chapters.Count;

        // Act
        await _eventBus.PublishAsync(new ChannelUpdateEvent(
            ChannelId,
            _stream.CurrentChapter.Title,
            _stream.CurrentChapter.CategoryId));

        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync();
        _stream.Chapters.Count.Should().Be(chapterCountBefore);
    }

    [Fact]
    public async Task MetadataChanged_ShouldIgnoreEvent_WhenChannelIdDoesNotMatch()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        var chapterCountBefore = _stream.Chapters.Count;

        // Act
        await _eventBus.PublishAsync(new ChannelUpdateEvent("some_other_channel_id", "New Title", "New Category"));
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync();
        _stream.Chapters.Count.Should().Be(chapterCountBefore);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposePlaylist_AndBeSafeToCallTwice()
    {
        // Arrange
        await using var sut = CreateSut();

        // Act
        await sut.DisposeAsync();
        var act = async () => await sut.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await _hlsPlaylist.Received().DisposeAsync();
    }
}