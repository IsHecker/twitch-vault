using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using FluentAssertions;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class PlaylistVariantTrackerTests
{
    private const string ChannelName = "testchannel";

    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly ILogger<PlaylistVariantTracker> _logger = Substitute.For<ILogger<PlaylistVariantTracker>>();

    private PlaylistVariantTracker CreateSut()
    {
        return new(ChannelName, _twitchGqlClient, _dateTimeProvider, _logger);
    }

    [Fact]
    public async Task GetVariantsAsync_ShouldCallTwitchApi_OnFirstPoll()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        var sut = CreateSut();
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        await sut.GetVariantsAsync(default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVariantsAsync_ShouldNotCallTwitchApi_WhenWithinRefreshInterval()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        var sut = CreateSut();
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        await sut.GetVariantsAsync(default);
        _twitchGqlClient.ClearReceivedCalls();

        await sut.GetVariantsAsync(default);

        // Assert
        await _twitchGqlClient.DidNotReceive().GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVariantsAsync_ShouldCallTwitchApi_WhenRefreshIntervalHasPassed()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        var sut = CreateSut();
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        await sut.GetVariantsAsync(default);
        _twitchGqlClient.ClearReceivedCalls();

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddSeconds(30));
        await sut.GetVariantsAsync(default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task GetVariantsAsync_ShouldReturnEmptyArray_WhenApiReturnsEmptyPlaylist()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        var sut = CreateSut();
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        var variants = await sut.GetVariantsAsync(default);

        // Assert
        variants.Should().BeEmpty();
    }

    // --- Stabilization Logic ---

    [Fact]
    public async Task GetVariantsAsync_ShouldNotStabilize_BeforeMinimumPollsReached()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        var sut = CreateSut();

        var playlist = CreatePlaylist(5);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(playlist);

        // Act
        for (int i = 0; i < 4; i++)
        {
            await sut.GetVariantsAsync(default);
            _dateTimeProvider.DateTimeNow.Returns(startTime.AddSeconds((i + 1) * 30));
        }
        _twitchGqlClient.ClearReceivedCalls();
        await sut.GetVariantsAsync(default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    private static string CreatePlaylist(int variantCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        for (int i = 0; i < variantCount; i++)
        {
            sb.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={1000 * (i + 1)}");
            sb.AppendLine($"http://twitch.tv/quality_{i}.m3u8");
        }
        return sb.ToString();
    }
}