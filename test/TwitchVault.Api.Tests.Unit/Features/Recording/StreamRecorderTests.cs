using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Tests.Unit.Features.Recording;

using Microsoft.Extensions.Time.Testing;

public class StreamRecorderTests : IDisposable
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";
    private const int MaxConsecutiveEmptyPolls = 3;

    private readonly IThumbnailManager _thumbnailManager = Substitute.For<IThumbnailManager>();
    private readonly IManifestPoller _manifestPoller = Substitute.For<IManifestPoller>();
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ISegmentStore _segmentStore = Substitute.For<ISegmentStore>();
    private readonly IHlsPlaylistWriter _hlsPlaylist = Substitute.For<IHlsPlaylistWriter>();
    private readonly ISegmentUploader _uploader = Substitute.For<ISegmentUploader>();
    private readonly IStreamFinalizer _finalizer = Substitute.For<IStreamFinalizer>();
    private readonly ILogger<StreamRecorder> _logger = Substitute.For<ILogger<StreamRecorder>>();
    private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
    private readonly IChapterTracker _chapterTracker = Substitute.For<IChapterTracker>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly SemaphoreSlim _pollStarted = new(0, 1);

    private readonly Channel _channel = Channel.Create(ChannelId, ChannelName, 1, isArchived: false);
    private readonly Api.Features.Streams.Stream _stream;

    public StreamRecorderTests()
    {
        _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxConsecutiveEmptyPolls = MaxConsecutiveEmptyPolls });

        _stream = Api.Features.Streams.Stream.Create(
            "ts_1",
            ChannelId,
            StreamFolder.Create("streams_root", ChannelName),
            new DateTime(2026, 1, 1),
            "Some Title",
            "Some Category");

        long lastSequence = 0;
        bool hasInit = true;
        string? lastSegmentFileName = null;

        _hlsPlaylist.LastTwitchMediaSequence.Returns(_ => lastSequence);
        _hlsPlaylist.HasInitSegment.Returns(_ => hasInit);
        _hlsPlaylist.LastSegmentFileName.Returns(_ => lastSegmentFileName);

        _hlsPlaylist.When(x => x.UpdateTwitchMediaSequence(Arg.Any<long>()))
            .Do(c => lastSequence = c.Arg<long>());
        _hlsPlaylist.When(x => x.SetInitSegmentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => hasInit = true);
        _hlsPlaylist.When(x => x.AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>()))
            .Do(c => lastSegmentFileName = c.Arg<string>());

        _finalizer.FinalizeAsync(
            Arg.Any<Channel>(),
            Arg.Any<Api.Features.Streams.Stream>(),
            Arg.Any<long>(),
            Arg.Any<SessionEndReason>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task StartAsync_ShouldProduceStreamEndedReason_WhenManifestSignalsStreamEnded()
    {
        var feed = new LiveHlsPlaylistFeed(windowSize: 2, firstMediaSequence: 100);
        StubLiveManifest(feed,
            f => f.AppendSegments(("seg_1.ts", 6f)).EndStream(),
            f => { }); // re-fetch of the same ended window: nothing new -> finalize
        StubDownloadSegments(Segment("seg_1.ts", 6f));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
        await _manifestPoller.Received(2).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldReturnImmediately_WhenStreamEndsWithNoMediaSegments()
    {
        // Arrange
        StubManifestOnce(BuildManifest(segments: []));
        StubDownloadSegments();
        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
        await _manifestPoller.Received(1).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_ShouldDelegateStreamStoppedReason_AndCancelTheLoop()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);
        await _pollStarted.WaitAsync();

        // Act
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertFinalizedAsync(new SessionEndReason.StreamStopped());
    }

    [Fact]
    public async Task ShutdownAsync_ShouldDelegateServerShutdownReason_AndCancelTheLoop()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);
        await _pollStarted.WaitAsync();

        // Act
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertFinalizedAsync(new SessionEndReason.StreamStopped());
    }

    [Fact]
    public async Task StopAsync_ShouldTakePrecedence_EvenIfLoopWouldOtherwiseReportStreamEnded()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);
        await _pollStarted.WaitAsync();

        // Act
        await sut.StopAsync();
        var act = async () => await startTask;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _finalizer.DidNotReceive().FinalizeAsync(
            _channel, _stream, Arg.Any<long>(), Arg.Is(new SessionEndReason.StreamEnded()));
    }

    [Fact]
    public async Task StartAsync_ShouldRetryUntilExhausted_WhenManifestAlwaysEmptyOrNull()
    {
        // Arrange
        StubManifestOnce(string.Empty);
        await using var sut = CreateSut();

        // Act
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await _manifestPoller.Received(MaxConsecutiveEmptyPolls).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
    }

    [Fact]
    public async Task StartAsync_ShouldRetryUntilExhausted_WhenManifestReturnsEmptyResponseStreamWithHttpResponse()
    {
        // Arrange
        _manifestPoller.GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult((new ResponseStream(System.IO.Stream.Null, new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)), false)));
        await using var sut = CreateSut();

        // Act
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await _manifestPoller.Received(MaxConsecutiveEmptyPolls).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
    }

    [Fact]
    public async Task StartAsync_ShouldCaptureAThumbnail_OnEveryPollingAttempt()
    {
        // Arrange
        StubManifestOnce(string.Empty);
        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _thumbnailManager.Received(MaxConsecutiveEmptyPolls)
            .TryCaptureSnapshotAsync(ChannelName, _stream, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldCloseSegmentAndAddDiscontinuity_WhenQualityChanges()
    {
        // Arrange
        _segmentStore.CloseCurrentSegment().Returns(new LocalSegment("seg_5.ts", 12.3f, 0), (LocalSegment?)null);
        StubManifestOnce(DefaultManifest, hasQualityChanged: true);
        StubDownloadSegments();

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_5.ts", 12.3f, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddDiscontinuityAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldNotCloseSegmentOrAddDiscontinuity_WhenQualityHasNotChanged()
    {
        // Arrange
        StubManifestOnce(DefaultManifest, hasQualityChanged: false);
        StubDownloadSegments();

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _hlsPlaylist.DidNotReceive().AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _hlsPlaylist.DidNotReceive().AddDiscontinuityAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldSetInitSegment_WhenNoInitSegmentYetAndManifestProvidesOne()
    {
        // Arrange
        StubManifestOnce(DefaultManifest);
        _hlsPlaylist.HasInitSegment.Returns(false);
        StubDownloadSegments(Segment("init.mp4", 0f, isInit: true));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _hlsPlaylist.Received(1).SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldAddRegularSegment_WhenInitSegmentAlreadyPresent()
    {
        // Arrange
        StubManifestOnce(DefaultManifest);
        _hlsPlaylist.HasInitSegment.Returns(true);
        StubDownloadSegments(Segment("seg_1.ts", 6f));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _hlsPlaylist.DidNotReceive().SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldUpdateTwitchMediaSequence()
    {
        // Arrange
        StubManifestOnce(DefaultManifest);
        StubDownloadSegments(Segment("seg_1.ts", 6f));
        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        _hlsPlaylist.Received(1).UpdateTwitchMediaSequence(100L);
    }

    [Fact]
    public async Task StartAsync_ShouldAccumulateStreamSizeBytes_FromStoredAndFinalSegments()
    {
        // Arrange
        StubManifestOnce(BuildManifest(segments: [("seg_1.ts", 6f)]));
        StubDownloadSegments(Segment("seg_1.ts", 6f, sizeBytes: 500));
        _segmentStore.CloseCurrentSegment().Returns(new LocalSegment("seg_2.ts", 2f, 250));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded(), expectedSizeBytes: 750);
        await _uploader.Received(1).AddAsync(Arg.Is<LocalSegment>(s => s.FilePath == "seg_1.ts" && s.SizeBytes == 500));
        await _uploader.Received(1).AddAsync(Arg.Is<LocalSegment>(s => s.FilePath == "seg_2.ts" && s.SizeBytes == 250));
    }

    [Fact]
    public async Task StartAsync_ShouldProduceStreamError_WhenManifestPollerThrows()
    {
        // Arrange
        var exception = new HttpRequestException("simulated network blip");
        _manifestPoller
            .GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);

        await using var sut = CreateSut();

        // Act
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await _manifestPoller.Received(1).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(new SessionEndReason.StreamError(exception));
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
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await AssertFinalizedAsync(new SessionEndReason.StreamError(exception));
    }

    [Fact]
    public async Task StartAsync_ShouldSkipSegmentAndContinue_WhenSegmentDownloadThrowsNetworkError()
    {
        // Arrange
        StubManifestOnce(DefaultManifest);
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection reset by peer"));

        await using var sut = CreateSut();

        // Act
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await _hlsPlaylist.DidNotReceive().AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(r => r is SessionEndReason.StreamEnded);
    }

    [Fact]
    public async Task StartAsync_ShouldContinueRecording_WhenSegmentStoreThrows()
    {
        // Arrange
        StubManifestOnce(DefaultManifest);
        StubDownloadSegments(Segment("seg_1.ts", 6f));
        _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Disk I/O error"));

        await using var sut = CreateSut();

        // Act
        var act = async () => await StartAndRunToCompletionAsync(sut);

        // Assert
        await act.Should().NotThrowAsync();
        await AssertFinalizedAsync(r => r is SessionEndReason.StreamEnded);
    }

    [Fact]
    public async Task StartAsync_ShouldAttachChapterTrackerAndSegmentUploader_BeforeRecordingBegins()
    {
        // Arrange
        BlockPollIndefinitely();
        await using var sut = CreateSut();
        var startTask = sut.StartAsync(_stream, _channel);
        await _pollStarted.WaitAsync();

        // Assert
        _chapterTracker.Received(1).Attach(_stream, _channel);
        _uploader.Received(1).Attach(_stream);

        // Cleanup
        await sut.StopAsync();
        await ((Func<Task>)(async () => await startTask)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeAllOwnedResources_AndBeSafeToCallTwice()
    {
        // Arrange
        await using var sut = CreateSut();

        // Act
        await sut.DisposeAsync();
        var act = async () => await sut.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await _hlsPlaylist.Received().DisposeAsync();
        _uploader.Received().Dispose();
        _chapterTracker.Received().Dispose();
    }

    // ---------------------------------------------------------------------
    // Live-playlist tests: these exercise PlaylistSegmentExtractor's real
    // sliding-window dedup logic across multiple polls. LastTwitchMediaSequence
    // is made stateful (StubStatefulMediaSequence) so the extractor is told
    // the truth about what's already been consumed, exactly as the real
    // IHlsPlaylistWriter would report it.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_ShouldAddEachSegmentExactlyOnce_AsTheyArriveAcrossASlidingLivePlaylist()
    {
        // Arrange: seg_1 rolls out of the 2-wide window once seg_3 arrives,
        // exactly like a real live Twitch playlist. If sequence tracking were
        // static (as in the pre-existing StubManifestOnce path), seg_1/seg_2
        // would get silently re-added on the second poll.
        StubStatefulMediaSequence();
        var feed = new LiveHlsPlaylistFeed(windowSize: 2, firstMediaSequence: 100);
        StubLiveManifest(feed,
            f => f.AppendSegments(("seg_1.ts", 6f)),
            f => f.AppendSegments(("seg_2.ts", 6f), ("seg_3.ts", 6f)),
            f => f.EndStream());
        StubDownloadSegments(Segment("seg_1.ts", 6f), Segment("seg_2.ts", 6f), Segment("seg_3.ts", 6f));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        Received.InOrder(() =>
        {
            _hlsPlaylist.AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
            _hlsPlaylist.AddSegmentAsync("seg_2.ts", 6f, Arg.Any<CancellationToken>());
            _hlsPlaylist.AddSegmentAsync("seg_3.ts", 6f, Arg.Any<CancellationToken>());
        });
        await _hlsPlaylist.Received(3).AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
    }

    [Fact]
    public async Task StartAsync_ShouldNotReAddSegments_WhenPollFindsNoNewSegmentsInTheWindow()
    {
        // Arrange: two polls see the exact same window (nothing new arrived
        // yet), then a third poll adds a genuinely new segment before ending.
        // Only the new segment should ever reach AddSegmentAsync.
        StubStatefulMediaSequence();
        var feed = new LiveHlsPlaylistFeed(windowSize: 3, firstMediaSequence: 100);
        StubLiveManifest(feed,
            f => f.AppendSegments(("seg_1.ts", 6f)),
            f => { /* no-op poll: nothing new arrived */ },
            f => f.AppendSegments(("seg_2.ts", 6f)),
            f => f.EndStream());
        StubDownloadSegments(Segment("seg_1.ts", 6f), Segment("seg_2.ts", 6f));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(1).AddSegmentAsync("seg_2.ts", 6f, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(2).AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldWaitAndKeepPolling_WhileStreamEndedFlagIsSetButSegmentsStillRemainUnconsumed()
    {
        // Arrange: real Twitch behavior -- EXT-X-ENDLIST can appear alongside
        // segments that haven't been fully drained from the window yet. The
        // recorder should keep polling (StreamEndWaitInterval) rather than
        // finalizing immediately, only stopping once no non-init segments
        // remain in a still-ended manifest.
        StubStatefulMediaSequence();
        var feed = new LiveHlsPlaylistFeed(windowSize: 2, firstMediaSequence: 100);
        StubLiveManifest(feed,
            f => f.AppendSegments(("seg_1.ts", 6f), ("seg_2.ts", 6f)).EndStream(),
            f => { }); // second poll: same ended manifest, no new segments -> should finalize here
        StubDownloadSegments(Segment("seg_1.ts", 6f), Segment("seg_2.ts", 6f));

        await using var sut = CreateSut();

        // Act
        await StartAndRunToCompletionAsync(sut);

        // Assert
        await _manifestPoller.Received(2).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
        await _hlsPlaylist.Received(2).AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
    }

    public void Dispose() => _pollStarted.Dispose();

    private StreamRecorder CreateSut(CancellationToken parentToken = default) =>
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
            _timeProvider,
            parentToken);

    private static string BuildManifest(
        long mediaSequence = 100,
        string? initUri = "init.mp4",
        (string FileName, float DurationSeconds)[]? segments = null,
        bool endList = true)
    {
        segments ??= [("seg_1.ts", 6f)];

        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append($"#EXT-X-MEDIA-SEQUENCE:{mediaSequence}\n");

        if (initUri is not null)
            sb.Append($"#EXT-X-MAP:URI=\"{initUri}\"\n");

        foreach (var (fileName, duration) in segments)
        {
            sb.Append($"#EXTINF:{duration:0.000},\n");
            sb.Append($"{fileName}\n");
        }

        if (endList)
            sb.Append("#EXT-X-ENDLIST\n");

        return sb.ToString();
    }

    private static readonly string DefaultManifest = BuildManifest();

    /// <summary>
    /// Simulates a real, growing live HLS media playlist the way Twitch
    /// actually serves one: a sliding EXT-X-MEDIA-SEQUENCE window that only
    /// ever grows, with older segments falling out of the window as new ones
    /// arrive. Nothing about tag placement or segment shape is special-cased
    /// here -- PlaylistSegmentExtractor parses the rendered text exactly as
    /// it would parse a real Twitch response.
    /// </summary>
    private sealed class LiveHlsPlaylistFeed(string? initUri = "init.mp4", int windowSize = 3, long firstMediaSequence = 100)
    {
        private readonly List<(string FileName, float Duration)> _allSegments = [];
        private bool _ended;

        public LiveHlsPlaylistFeed AppendSegments(params (string FileName, float Duration)[] segments)
        {
            if (_ended)
                throw new InvalidOperationException("Cannot append segments after the simulated stream has ended.");
            _allSegments.AddRange(segments);
            return this;
        }

        public LiveHlsPlaylistFeed EndStream()
        {
            _ended = true;
            return this;
        }

        /// <summary>
        /// Renders the manifest exactly as it would look "right now": only
        /// the most recent <c>windowSize</c> segments are present, with the
        /// media sequence advanced to match whatever rolled out of the window.
        /// </summary>
        public string RenderCurrentManifest()
        {
            var windowStart = Math.Max(0, _allSegments.Count - windowSize);
            var windowSegments = _allSegments.Skip(windowStart).ToArray();

            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n");
            sb.Append($"#EXT-X-MEDIA-SEQUENCE:{firstMediaSequence + windowStart}\n");
            if (initUri is not null)
                sb.Append($"#EXT-X-MAP:URI=\"{initUri}\"\n");

            foreach (var (fileName, duration) in windowSegments)
            {
                sb.Append($"#EXTINF:{duration:0.000},\n");
                sb.Append($"{fileName}\n");
            }

            if (_ended)
                sb.Append("#EXT-X-ENDLIST\n");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Makes the mocked playlist writer remember its sequence number the way
    /// the real one would, so ExtractNewSegmentsAsync's dedup logic is
    /// genuinely exercised across polls instead of re-seeing the same
    /// segments forever (which is what a fixed .Returns(0L) would cause).
    /// </summary>
    private void StubStatefulMediaSequence()
    {
        long lastSequence = 0;
        _hlsPlaylist.LastTwitchMediaSequence.Returns(_ => lastSequence);
        _hlsPlaylist.When(x => x.UpdateTwitchMediaSequence(Arg.Any<long>()))
            .Do(callInfo => lastSequence = callInfo.Arg<long>());
    }

    /// <summary>
    /// Feeds one "poll step" per call to GetNextManifestAsync, running
    /// <paramref name="steps"/> in order. Extra polls beyond the given steps
    /// just re-render the feed's current, unchanged state -- exactly like a
    /// live poll that finds nothing new.
    /// </summary>
    private void StubLiveManifest(LiveHlsPlaylistFeed feed, params Action<LiveHlsPlaylistFeed>[] steps)
    {
        var stepIndex = 0;
        _manifestPoller.GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (stepIndex < steps.Length)
                    steps[stepIndex++](feed);

                var manifest = feed.RenderCurrentManifest();
                var responseStream = new ResponseStream(
                    new MemoryStream(Encoding.UTF8.GetBytes(manifest)),
                    new HttpResponseMessage());
                return Task.FromResult((responseStream, HasQualityChanged: false));
            });
    }

    // GetNextManifestAsync returns (ResponseStream, bool) instead of
    // (string?, bool). A manifest is turned into a ResponseStream wrapping a
    // fresh MemoryStream (ExtractNewSegmentsAsync disposes the stream after
    // reading, so each call needs its own instance). "No manifest" -- either
    // an empty/null manifest, or any call after the first -- is represented
    // by ResponseStream.Null, matching the real ManifestPoller's contract.
    private void StubManifestOnce(string manifest, bool hasQualityChanged = false)
    {
        var called = false;
        _manifestPoller.GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (called || string.IsNullOrEmpty(manifest))
                    return Task.FromResult((ResponseStream.Null, false));

                called = true;
                var responseStream = new ResponseStream(
                    new MemoryStream(Encoding.UTF8.GetBytes(manifest)),
                    new HttpResponseMessage());
                return Task.FromResult((responseStream, hasQualityChanged));
            });
    }

    private static (string FileName, float Duration, bool IsInit, long SizeBytes) Segment(
        string fileName, float duration, bool isInit = false, long sizeBytes = 0) =>
        (fileName, duration, isInit, sizeBytes);

    private void StubDownloadSegments(params (string FileName, float Duration, bool IsInit, long SizeBytes)[] items)
    {
        // DownloadAsStreamAsync now returns ResponseStream instead of a bare Stream.
        _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ResponseStream(new MemoryStream([1, 2, 3]), new HttpResponseMessage()));

        if (items.Length == 0)
        {
            _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns((LocalSegment?)null);
            return;
        }

        var queue = new Queue<(string FileName, float Duration, bool IsInit, long SizeBytes)>(items);
        _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => queue.TryDequeue(out var item)
                ? new LocalSegment(item.FileName, item.Duration, item.SizeBytes)
                : null);
    }

    private void BlockPollIndefinitely() =>
        _manifestPoller
            .GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                _pollStarted.Release();
                var token = callInfo.Arg<CancellationToken>();
                await Task.Delay(Timeout.Infinite, token);
                return (ResponseStream.Null, false);
            });

    private async Task RunToCompletionAsync(Task task, TimeSpan? realTimeTimeout = null)
    {
        var deadline = DateTime.UtcNow + (realTimeTimeout ?? TimeSpan.FromSeconds(5));
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    "StreamRecorder did not complete after advancing the fake clock. " +
                    "Check that every delay in RecordStreamAsync uses the injected TimeProvider.");

            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        await task;
    }

    private Task StartAndRunToCompletionAsync(StreamRecorder sut) =>
        RunToCompletionAsync(sut.StartAsync(_stream, _channel));

    private Task AssertFinalizedAsync(SessionEndReason expectedReason, long expectedSizeBytes = 0) =>
        _finalizer.Received(1).FinalizeAsync(_channel, _stream, expectedSizeBytes, Arg.Is(expectedReason));

    private Task AssertFinalizedAsync(Func<SessionEndReason, bool> matches, long expectedSizeBytes = 0) =>
        _finalizer.Received(1).FinalizeAsync(_channel, _stream, expectedSizeBytes, Arg.Is<SessionEndReason>(r => matches(r)));
}