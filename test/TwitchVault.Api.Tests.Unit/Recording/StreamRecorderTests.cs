using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Events;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class StreamRecorderTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";
    private const string Manifest =
        "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:100\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXT-X-ENDLIST\n";

    private readonly IThumbnailManager _thumbnailManager = Substitute.For<IThumbnailManager>();
    private readonly IManifestPoller _manifestPoller = Substitute.For<IManifestPoller>();
    private readonly ISegmentDownloader _segmentDownloader = Substitute.For<ISegmentDownloader>();
    private readonly IHlsPlaylist _hlsPlaylist = Substitute.For<IHlsPlaylist>();
    private readonly IStreamRepository _streamRepository = Substitute.For<IStreamRepository>();
    private readonly IStreamFinalizer _finalizer = Substitute.For<IStreamFinalizer>();
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly EventBus _eventBus;
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly ILogger<StreamRecorder> _logger = Substitute.For<ILogger<StreamRecorder>>();
    private readonly SettingsService _settingsService;

    private readonly Channel _channel = new() { Id = ChannelId, Name = ChannelName, QualityRank = 1 };
    private readonly Domain.Stream _stream;

    public StreamRecorderTests()
    {
        _pathsOptions.Value.Returns(new PathsOptions { Settings = "non_existent_file.json" });
        _settingsService = new SettingsService(_pathsOptions);
        _settingsService.Settings.Vault.MaxConsecutiveEmptyPolls = 3;

        _eventBus = new EventBus(Substitute.For<ILogger<EventBus>>());

        _stream = new Domain.Stream { TwitchStreamId = "ts_1", ChannelId = ChannelId };
        _stream.AddChapter("Some Title", "Some Category", new DateTime(2026, 1, 1));
        _stream.Folder = StreamFolder.Create("streams_root", ChannelName);

        _hlsPlaylist.LastTwitchMediaSequence.Returns(0L);
        _hlsPlaylist.HasInitSegment.Returns(true);

        _finalizer.FinalizeAsync(
            Arg.Any<Domain.Stream>(),
            Arg.Any<Channel>(),
            Arg.Any<ISegmentDownloader>(),
            Arg.Any<SessionEndReason>()).Returns(Task.CompletedTask);
    }

    private StreamRecorder CreateSut(CancellationToken parentToken = default) =>
        new(_thumbnailManager,
            _manifestPoller,
            _segmentDownloader,
            _hlsPlaylist,
            _streamRepository,
            _finalizer,
            _settingsService,
            _pathsOptions,
            _eventBus,
            _dateTimeProvider,
            _logger,
            parentToken);


    private string FormatSegmentUrl(string segmentName) =>
        $"{_pathsOptions.Value.BaseUrl}/hls/{_stream.TwitchStreamId}/segments/{segmentName}";

    private static async IAsyncEnumerable<(string FileName, float Duration)> Segments(
        params (string FileName, float Duration)[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }

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

    private void StubDownloadSegments(params (string FileName, float Duration)[] items) =>
        _segmentDownloader
            .DownloadSegmentsAsync(Arg.Any<string>(), Arg.Any<ManifestExtractionResult>(), _hlsPlaylist, Arg.Any<CancellationToken>())
            .Returns(Segments(items));

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
            _stream, _channel, _segmentDownloader, Arg.Is(new SessionEndReason.StreamEnded()));
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
            _stream, _channel, _segmentDownloader, Arg.Is(new SessionEndReason.StreamStopped()));
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
                Arg.Any<Domain.Stream>(),
                Arg.Any<Channel>(),
                Arg.Any<ISegmentDownloader>(),
                Arg.Any<SessionEndReason.StreamEnded>());
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
            _stream, _channel, _segmentDownloader, Arg.Any<SessionEndReason.StreamEnded>());
    }

    [Fact]
    public async Task StartAsync_ShouldCloseSegmentAndAddDiscontinuity_WhenQualityChanges()
    {
        // Arrange
        _segmentDownloader.CloseSegment().Returns(("seg_5.ts", 12.3f));
        StubManifestOnce(Manifest, hasQualityChanged: true);
        StubDownloadSegments();

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.Received(1).AddSegmentAsync(FormatSegmentUrl("seg_5.ts"), 12.3f, Arg.Any<CancellationToken>());
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
        _segmentDownloader
            .DownloadSegmentsAsync(Arg.Any<string>(), Arg.Any<ManifestExtractionResult>(), _hlsPlaylist, Arg.Any<CancellationToken>())
            .Returns(Segments(("init.mp4", 0f)));

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.Received(1).SetInitSegmentAsync(FormatSegmentUrl("init.mp4"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldAddRegularSegment_WhenInitSegmentAlreadyPresent()
    {
        // Arrange
        StubManifestOnce(Manifest);
        _hlsPlaylist.HasInitSegment.Returns(true);
        _segmentDownloader
            .DownloadSegmentsAsync(Arg.Any<string>(), Arg.Any<ManifestExtractionResult>(), _hlsPlaylist, Arg.Any<CancellationToken>())
            .Returns(Segments(("seg_1.ts", 6f)));

        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        await _hlsPlaylist.Received(1).AddSegmentAsync(FormatSegmentUrl("seg_1.ts"), 6f, Arg.Any<CancellationToken>());
        await _hlsPlaylist.DidNotReceive().SetInitSegmentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldUpdateTwitchMediaSequence()
    {
        // Arrange
        StubManifestOnce(Manifest);
        StubDownloadSegments();
        await using var sut = CreateSut();

        // Act
        await sut.StartAsync(_stream, _channel);

        // Assert
        _hlsPlaylist.Received().UpdateTwitchMediaSequence(Arg.Any<long>());
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
        await _thumbnailManager.Received(1).TryCaptureSnapshotAsync(ChannelName, _stream, CancellationToken.None);
    }


    [Fact]
    public async Task StartAsync_ShouldProduceStreamError_WhenNetworkErrorsExceedRetryLimit()
    {
        // Arrange
        _manifestPoller
            .GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException("simulated network blip"));

        await using var sut = CreateSut();

        // Act
        var act = async () => await sut.StartAsync(_stream, _channel);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _manifestPoller.Received(6).GetNextManifestAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldDelegateStreamError_AndStillFinalize()
    {
        // Arrange
        _thumbnailManager.TryCaptureSnapshotAsync(ChannelName, Arg.Any<Domain.Stream>(), CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        await using var sut = CreateSut();

        // Act
        var act = async () => await sut.StartAsync(_stream, _channel);

        // Assert
        await act.Should().NotThrowAsync();
        await _finalizer.Received(1).FinalizeAsync(
            _stream, _channel, _segmentDownloader, Arg.Any<SessionEndReason.StreamError>());
    }


    [Fact]
    public async Task MetadataChanged_ShouldAddNewChapter_WhileSessionIsStillActive()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        var chapterCountBefore = _stream.Chapters.Count;

        // Act
        await _eventBus.PublishAsync(new ChannelUpdateEvent(ChannelId, "New Title", "New Category"));
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync();
        _stream.Chapters.Count.Should().Be(chapterCountBefore + 1);
        _stream.CurrentChapter.Title.Should().Be("New Title");
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
        await _streamRepository.Received(1).UpdateAsync(_stream);
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
    public async Task MetadataChanged_ShouldIgnoreEvent_WhenTitleOrCategoryDiffers()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);

        var chapterCountBefore = _stream.Chapters.Count;
        var currentCategory = _stream.CurrentChapter.CategoryId;

        // Act
        await _eventBus.PublishAsync(new ChannelUpdateEvent(ChannelId, "Different Title", currentCategory));
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync();
        _stream.Chapters.Count.Should().Be(chapterCountBefore + 1);

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