using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Persistence.Database;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using FluentAssertions;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class ManifestPollerTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";

    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly ILogger<ManifestPoller> _logger = Substitute.For<ILogger<ManifestPoller>>();
    private readonly TestDbContextFactory _factory = new();
    private readonly Channel _channel = Channel.Create(ChannelId, ChannelName, 1, isArchived: false);

    private ManifestPoller CreateSut(AppDbContext db)
    {
        if (!db.Channels.Any(c => c.Name == ChannelName))
        {
            db.Channels.Add(Channel.Create("54507525", ChannelName, 1, isArchived: false));
            db.SaveChanges();
        }

        _twitchGqlClient.GetPlaylistContentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("some_manifest");
        return new(_twitchGqlClient, _dateTimeProvider, _logger);
    }

    [Fact]
    public async Task GetNextManifestAsync_ShouldCallTwitchApi_OnFirstPoll()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        await sut.GetNextManifestAsync(_channel, default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNextManifestAsync_ShouldNotCallTwitchApi_WhenWithinRefreshInterval()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(CreatePlaylist(5));

        // Act
        await sut.GetNextManifestAsync(_channel, default);
        _twitchGqlClient.ClearReceivedCalls();

        await sut.GetNextManifestAsync(_channel, default);

        // Assert
        await _twitchGqlClient.DidNotReceive().GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNextManifestAsync_ShouldCallTwitchApi_WhenRefreshIntervalHasPassed()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(CreatePlaylist(5));

        // Act
        await sut.GetNextManifestAsync(_channel, default);
        _twitchGqlClient.ClearReceivedCalls();

        _dateTimeProvider.DateTimeNow.Returns(startTime.AddSeconds(30));
        await sut.GetNextManifestAsync(_channel, default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNextManifestAsync_ShouldReturnNullManifest_WhenApiReturnsEmptyPlaylist()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        var (Manifest, HasQualityChanged) = await sut.GetNextManifestAsync(_channel, default);

        // Assert
        Manifest.Should().BeNull();
        HasQualityChanged.Should().BeFalse();
    }

    // --- Stabilization Logic ---

    [Fact]
    public async Task GetNextManifestAsync_ShouldNotStabilize_BeforeMinimumPollsReached()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);

        var playlist = CreatePlaylist(5);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(playlist);

        // Act
        for (int i = 0; i < 4; i++)
        {
            await sut.GetNextManifestAsync(_channel, default);
            _dateTimeProvider.DateTimeNow.Returns(startTime.AddSeconds((i + 1) * 30));
        }
        _twitchGqlClient.ClearReceivedCalls();
        await sut.GetNextManifestAsync(_channel, default);

        // Assert
        await _twitchGqlClient.Received(1).GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNextManifestAsync_ShouldStabilize_AfterMinimumPollsReached()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0);
        _dateTimeProvider.DateTimeNow.Returns(startTime);
        using var db = _factory.CreateDbContext();
        var sut = CreateSut(db);

        var playlist = CreatePlaylist(5);
        _twitchGqlClient.GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>()).Returns(playlist);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await sut.GetNextManifestAsync(_channel, default);
            _dateTimeProvider.DateTimeNow.Returns(startTime.AddSeconds((i + 1) * 30));
        }
        _twitchGqlClient.ClearReceivedCalls();
        await sut.GetNextManifestAsync(_channel, default);

        // Assert
        await _twitchGqlClient.DidNotReceive().GetMasterPlaylistAsync(ChannelName, Arg.Any<CancellationToken>());
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