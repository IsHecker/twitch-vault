// using System.Text;
// using FluentAssertions;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using NSubstitute;
// using NSubstitute.ExceptionExtensions;
// using TwitchVault.Api.Configuration;
// using TwitchVault.Api.Recording;
// using TwitchVault.Api.Recording.HLS;
// using TwitchVault.Api.Twitch;

// namespace TwitchVault.Api.Tests.Unit.Recording;

// using Microsoft.Extensions.Time.Testing;

// public class StreamRecorderTests : IDisposable
// {
//     private const string ChannelId = "54507525";
//     private const string ChannelName = "testchannel";
//     private const int MaxConsecutiveEmptyPolls = 3;

//     private readonly IThumbnailManager _thumbnailManager = Substitute.For<IThumbnailManager>();
//     private readonly IManifestPoller _manifestPoller = Substitute.For<IManifestPoller>();
//     private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
//     private readonly ISegmentStore _segmentStore = Substitute.For<ISegmentStore>();
//     private readonly IHlsPlaylistWriter _hlsPlaylist = Substitute.For<IHlsPlaylistWriter>();
//     private readonly ISegmentUploader _uploader = Substitute.For<ISegmentUploader>();
//     private readonly IStreamFinalizer _finalizer = Substitute.For<IStreamFinalizer>();
//     private readonly ILogger<StreamRecorder> _logger = Substitute.For<ILogger<StreamRecorder>>();
//     private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
//     private readonly IChapterTracker _chapterTracker = Substitute.For<IChapterTracker>();
//     private readonly FakeTimeProvider _timeProvider = new();
//     private readonly SemaphoreSlim _pollStarted = new(0, 1);

//     private readonly Channel _channel = Channel.Create(ChannelId, ChannelName, 1, isArchived: false);
//     private readonly Domain.Stream _stream;

//     public StreamRecorderTests()
//     {
//         _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxConsecutiveEmptyPolls = MaxConsecutiveEmptyPolls });

//         _stream = Domain.Stream.Create(
//             "ts_1",
//             ChannelId,
//             StreamFolder.Create("streams_root", ChannelName),
//             new DateTime(2026, 1, 1),
//             "Some Title",
//             "Some Category");

//         _hlsPlaylist.LastTwitchMediaSequence.Returns(0L);
//         _hlsPlaylist.HasInitSegment.Returns(true);

//         _finalizer.FinalizeAsync(
//             Arg.Any<Channel>(),
//             Arg.Any<Domain.Stream>(),
//             Arg.Any<long>(),
//             Arg.Any<SessionEndReason>()).Returns(Task.CompletedTask);
//     }

//     [Fact]
//     public async Task StartAsync_ShouldProduceStreamEndedReason_WhenManifestSignalsStreamEnded()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         StubDownloadSegments();
//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldReturnImmediately_WhenStreamEndsWithNoMediaSegments()
//     {
//         // Arrange
//         StubManifestOnce(BuildManifest(segments: []));
//         StubDownloadSegments();
//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
//         await _manifestPoller.Received(1).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StopAsync_ShouldDelegateStreamStoppedReason_AndCancelTheLoop()
//     {
//         // Arrange
//         BlockPollIndefinitely();
//         await using var sut = CreateSut();
//         var startTask = sut.StartAsync(_stream, _channel);
//         await _pollStarted.WaitAsync();

//         // Act
//         await sut.StopAsync();
//         var act = async () => await startTask;

//         // Assert
//         await act.Should().ThrowAsync<OperationCanceledException>();
//         await AssertFinalizedAsync(new SessionEndReason.StreamStopped());
//     }

//     [Fact]
//     public async Task StopAsync_ShouldTakePrecedence_EvenIfLoopWouldOtherwiseReportStreamEnded()
//     {
//         // Arrange
//         BlockPollIndefinitely();
//         await using var sut = CreateSut();
//         var startTask = sut.StartAsync(_stream, _channel);
//         await _pollStarted.WaitAsync();

//         // Act
//         await sut.StopAsync();
//         var act = async () => await startTask;

//         // Assert
//         await act.Should().ThrowAsync<OperationCanceledException>();
//         await _finalizer.DidNotReceive().FinalizeAsync(
//             _channel, _stream, Arg.Any<long>(), Arg.Is(new SessionEndReason.StreamEnded()));
//     }

//     [Fact]
//     public async Task StartAsync_ShouldRetryUntilExhausted_WhenManifestAlwaysEmptyOrNull()
//     {
//         // Arrange
//         StubManifestOnce(string.Empty);
//         await using var sut = CreateSut();

//         // Act
//         var act = async () => await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await act.Should().NotThrowAsync();
//         await _manifestPoller.Received(MaxConsecutiveEmptyPolls).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
//         await AssertFinalizedAsync(new SessionEndReason.StreamEnded());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldCaptureAThumbnail_OnEveryPollingAttempt()
//     {
//         // Arrange
//         StubManifestOnce(string.Empty);
//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await _thumbnailManager.Received(MaxConsecutiveEmptyPolls)
//             .TryCaptureSnapshotAsync(ChannelName, _stream, Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldCloseSegmentAndAddDiscontinuity_WhenQualityChanges()
//     {
//         // Arrange
//         _segmentStore.CloseCurrentSegment().Returns(new LocalSegment("seg_5.ts", 12.3f, 0), (LocalSegment?)null);
//         StubManifestOnce(DefaultManifest, hasQualityChanged: true);
//         StubDownloadSegments();

//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await _hlsPlaylist.Received(1).AddSegmentAsync("seg_5.ts", 12.3f, Arg.Any<CancellationToken>());
//         await _hlsPlaylist.Received(1).AddDiscontinuityAsync(Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldNotCloseSegmentOrAddDiscontinuity_WhenQualityHasNotChanged()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest, hasQualityChanged: false);
//         StubDownloadSegments();

//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await _hlsPlaylist.DidNotReceive().AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
//         await _hlsPlaylist.DidNotReceive().AddDiscontinuityAsync(Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldSetInitSegment_WhenNoInitSegmentYetAndManifestProvidesOne()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         _hlsPlaylist.HasInitSegment.Returns(false);
//         StubDownloadSegments(Segment("init.mp4", 0f, isInit: true));

//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await _hlsPlaylist.Received(1).SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldAddRegularSegment_WhenInitSegmentAlreadyPresent()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         _hlsPlaylist.HasInitSegment.Returns(true);
//         StubDownloadSegments(Segment("seg_1.ts", 6f));

//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await _hlsPlaylist.DidNotReceive().SetInitSegmentAsync("init.mp4", Arg.Any<CancellationToken>());
//         await _hlsPlaylist.Received(1).AddSegmentAsync("seg_1.ts", 6f, Arg.Any<CancellationToken>());
//     }

//     [Fact]
//     public async Task StartAsync_ShouldUpdateTwitchMediaSequence()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         StubDownloadSegments(Segment("seg_1.ts", 6f));
//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         _hlsPlaylist.Received(1).UpdateTwitchMediaSequence(100L);
//     }

//     [Fact]
//     public async Task StartAsync_ShouldAccumulateStreamSizeBytes_FromStoredAndFinalSegments()
//     {
//         // No previous test in this suite exercised this — every stub returned 0 bytes,
//         // so the size accumulation logic was silently untested.

//         // Arrange
//         StubManifestOnce(BuildManifest(segments: [("seg_1.ts", 6f)]));
//         StubDownloadSegments(Segment("seg_1.ts", 6f, sizeBytes: 500));
//         _segmentStore.CloseCurrentSegment().Returns(new LocalSegment("seg_2.ts", 2f, 250));

//         await using var sut = CreateSut();

//         // Act
//         await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await AssertFinalizedAsync(new SessionEndReason.StreamEnded(), expectedSizeBytes: 750);
//         await _uploader.Received(1).AddAsync(Arg.Is<LocalSegment>(s => s.FilePath == "seg_1.ts" && s.SizeBytes == 500));
//         await _uploader.Received(1).AddAsync(Arg.Is<LocalSegment>(s => s.FilePath == "seg_2.ts" && s.SizeBytes == 250));
//     }

//     [Fact]
//     public async Task StartAsync_ShouldProduceStreamError_WhenManifestPollerThrows()
//     {
//         // Arrange
//         var exception = new HttpRequestException("simulated network blip");
//         _manifestPoller
//             .GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
//             .ThrowsAsync(exception);

//         await using var sut = CreateSut();

//         // Act
//         var act = async () => await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await act.Should().NotThrowAsync();
//         // Polly retries happen inside ITwitchGqlClient/HttpClient, below IManifestPoller —
//         // mocking IManifestPoller bypasses that pipeline entirely, so only 1 call reaches here.
//         await _manifestPoller.Received(1).GetNextManifestAsync(_channel, Arg.Any<CancellationToken>());
//         await AssertFinalizedAsync(new SessionEndReason.StreamError(exception));
//     }

//     [Fact]
//     public async Task StartAsync_ShouldDelegateStreamError_AndStillFinalize()
//     {
//         // Arrange
//         var exception = new InvalidOperationException("boom");
//         _thumbnailManager.TryCaptureSnapshotAsync(ChannelName, _stream, Arg.Any<CancellationToken>())
//             .Returns(Task.FromException(exception));

//         await using var sut = CreateSut();

//         // Act
//         var act = async () => await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await act.Should().NotThrowAsync();
//         await AssertFinalizedAsync(new SessionEndReason.StreamError(exception));
//     }

//     [Fact]
//     public async Task StartAsync_ShouldSkipSegmentAndContinue_WhenSegmentDownloadThrowsNetworkError()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
//             .ThrowsAsync(new HttpRequestException("Connection reset by peer"));

//         await using var sut = CreateSut();

//         // Act
//         var act = async () => await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await act.Should().NotThrowAsync();
//         await _hlsPlaylist.DidNotReceive().AddSegmentAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
//         await AssertFinalizedAsync(r => r is SessionEndReason.StreamEnded);
//     }

//     [Fact]
//     public async Task StartAsync_ShouldContinueRecording_WhenSegmentStoreThrows()
//     {
//         // Arrange
//         StubManifestOnce(DefaultManifest);
//         StubDownloadSegments(Segment("seg_1.ts", 6f));
//         _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
//             .ThrowsAsync(new IOException("Disk I/O error"));

//         await using var sut = CreateSut();

//         // Act
//         var act = async () => await StartAndRunToCompletionAsync(sut);

//         // Assert
//         await act.Should().NotThrowAsync();
//         await AssertFinalizedAsync(r => r is SessionEndReason.StreamEnded);
//     }

//     [Fact]
//     public async Task StartAsync_ShouldAttachChapterTrackerAndSegmentUploader_BeforeRecordingBegins()
//     {
//         // Replaces the old "MetadataChanged_*" tests, which published to a local EventBus
//         // that StreamRecorder never receives and chapterTracker (a mock) never subscribes to —
//         // they passed regardless of whether chapter tracking worked. Chapter mutation on
//         // channel-update events belongs in ChapterTrackerTests, not here.

//         // Arrange
//         BlockPollIndefinitely();
//         await using var sut = CreateSut();
//         var startTask = sut.StartAsync(_stream, _channel);
//         await _pollStarted.WaitAsync();

//         // Assert
//         _chapterTracker.Received(1).Attach(_stream, _channel);
//         _uploader.Received(1).Attach(_stream);

//         // Cleanup
//         await sut.StopAsync();
//         await ((Func<Task>)(async () => await startTask)).Should().ThrowAsync<OperationCanceledException>();
//     }

//     [Fact]
//     public async Task DisposeAsync_ShouldDisposeAllOwnedResources_AndBeSafeToCallTwice()
//     {
//         // Arrange
//         await using var sut = CreateSut();

//         // Act
//         await sut.DisposeAsync();
//         var act = async () => await sut.DisposeAsync();

//         // Assert
//         await act.Should().NotThrowAsync();
//         // Received() not Received(1): `await using` disposes sut once more at the end of
//         // this test, so an exact count would be a false failure.
//         await _hlsPlaylist.Received().DisposeAsync();
//         _uploader.Received().Dispose();
//         _chapterTracker.Received().Dispose();
//     }


//     public void Dispose() => _pollStarted.Dispose();

//     private StreamRecorder CreateSut(CancellationToken parentToken = default) =>
//         new(_thumbnailManager,
//             _manifestPoller,
//             _twitchGqlClient,
//             _hlsPlaylist,
//             _segmentStore,
//             _chapterTracker,
//             _uploader,
//             _finalizer,
//             _vaultOptions,
//             _logger,
//             _timeProvider,
//             parentToken);

//     private static string BuildManifest(
//         long mediaSequence = 100,
//         string? initUri = "init.mp4",
//         (string FileName, float DurationSeconds)[]? segments = null,
//         bool endList = true)
//     {
//         segments ??= [("seg_1.ts", 6f)];

//         var sb = new StringBuilder();
//         sb.Append("#EXTM3U\n");
//         sb.Append($"#EXT-X-MEDIA-SEQUENCE:{mediaSequence}\n");

//         if (initUri is not null)
//             sb.Append($"#EXT-X-MAP:URI=\"{initUri}\"\n");

//         foreach (var (fileName, duration) in segments)
//         {
//             sb.Append($"#EXTINF:{duration:0.000},\n");
//             sb.Append($"{fileName}\n");
//         }

//         if (endList)
//             sb.Append("#EXT-X-ENDLIST\n");

//         return sb.ToString();
//     }

//     private static readonly string DefaultManifest = BuildManifest();

//     private void StubManifestOnce(string manifest, bool hasQualityChanged = false)
//     {
//         var called = false;
//         _manifestPoller.GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
//             .Returns(_ =>
//             {
//                 if (called)
//                     return Task.FromResult<(string?, bool)>((null, false));

//                 called = true;
//                 return Task.FromResult<(string?, bool)>((manifest, hasQualityChanged));
//             });
//     }

//     private static (string FileName, float Duration, bool IsInit, long SizeBytes) Segment(
//         string fileName, float duration, bool isInit = false, long sizeBytes = 0) =>
//         (fileName, duration, isInit, sizeBytes);

//     private void StubDownloadSegments(params (string FileName, float Duration, bool IsInit, long SizeBytes)[] items)
//     {
//         _twitchGqlClient.DownloadAsStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
//             .Returns(_ => new MemoryStream([1, 2, 3]));

//         if (items.Length == 0)
//         {
//             _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
//                 .Returns((LocalSegment?)null);
//             return;
//         }

//         var queue = new Queue<(string FileName, float Duration, bool IsInit, long SizeBytes)>(items);
//         _segmentStore.SaveAsync(Arg.Any<string>(), Arg.Any<SegmentContent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
//             .Returns(_ => queue.TryDequeue(out var item)
//                 ? new LocalSegment(item.FileName, item.Duration, item.SizeBytes)
//                 : null);
//     }

//     private void BlockPollIndefinitely() =>
//         _manifestPoller
//             .GetNextManifestAsync(_channel, Arg.Any<CancellationToken>())
//             .Returns(async callInfo =>
//             {
//                 _pollStarted.Release();
//                 var token = callInfo.Arg<CancellationToken>();
//                 await Task.Delay(Timeout.Infinite, token);
//                 return (null, false);
//             });

//     private async Task RunToCompletionAsync(Task task, TimeSpan? realTimeTimeout = null)
//     {
//         var deadline = DateTime.UtcNow + (realTimeTimeout ?? TimeSpan.FromSeconds(5));
//         while (!task.IsCompleted)
//         {
//             if (DateTime.UtcNow > deadline)
//                 throw new TimeoutException(
//                     "StreamRecorder did not complete after advancing the fake clock. " +
//                     "Check that every delay in RecordStreamAsync uses the injected TimeProvider.");

//             _timeProvider.Advance(TimeSpan.FromSeconds(1));
//             await Task.Yield();
//         }

//         await task;
//     }

//     private Task StartAndRunToCompletionAsync(StreamRecorder sut) =>
//         RunToCompletionAsync(sut.StartAsync(_stream, _channel));

//     private Task AssertFinalizedAsync(SessionEndReason expectedReason, long expectedSizeBytes = 0) =>
//         _finalizer.Received(1).FinalizeAsync(_channel, _stream, expectedSizeBytes, Arg.Is(expectedReason));

//     private Task AssertFinalizedAsync(Func<SessionEndReason, bool> matches, long expectedSizeBytes = 0) =>
//         _finalizer.Received(1).FinalizeAsync(_channel, _stream, expectedSizeBytes, Arg.Is<SessionEndReason>(r => matches(r)));
// }