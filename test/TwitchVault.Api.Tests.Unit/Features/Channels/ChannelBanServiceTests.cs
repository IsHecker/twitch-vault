using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TwitchVault.Api.Features.Streams;

namespace TwitchVault.Api.Tests.Unit.Features.Channels;

public class ChannelBanServiceTests : ChannelTestBase
{
    private ChannelBanService CreateSut() =>
        new(Db, TwitchGqlClient, TwitchSubscription, RecordingOrchestrator, PathsOptions, DateTimeProvider);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BanChannelAsync_ShouldReturnValidation_WhenNameIsEmpty(string channelName)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync(channelName, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("bad name")]
    [InlineData("../../etc/passwd")]
    public async Task BanChannelAsync_ShouldReturnValidation_WhenNameIsNotAValidTwitchLogin(string channelName)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync(channelName, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await TwitchGqlClient.DidNotReceive().GetChannelIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BanChannelAsync_ShouldReturnNotFound_WhenChannelIsUnknownToTwitch()
    {
        // Arrange
        TwitchGqlClient.GetChannelIdAsync("ghostchannel", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("GhostChannel", null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task BanChannelAsync_ShouldResolveKnownChannelByName_WithoutHittingTwitch()
    {
        // Arrange
        Db.Channels.Add(Channel.Create("chan_known", "knownchan", 2, isArchived: false));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("KnownChan", "spam");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("chan_known");
        result.Value.ChannelName.Should().Be("knownchan");
        await TwitchGqlClient.DidNotReceive().GetChannelIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BanChannelAsync_ShouldBanUnmonitoredChannel_ResolvedThroughTwitch()
    {
        // Arrange
        ChannelExistsOnTwitch("neverseen", "chan_never_seen");
        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("neverseen", "pre-emptive ban");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("chan_never_seen");
        (await Db.BannedChannels.AnyAsync(b => b.Id == "chan_never_seen")).Should().BeTrue();
        await TwitchSubscription.DidNotReceive().RemoveChannelAsync(Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BanChannelAsync_ShouldRemoveNonAdminSubscriptions_AndKeepChannel_WhenAdminIsSubscribed()
    {
        // Arrange
        var regularUser = await CreateUserAsync();
        var adminUser = await CreateUserAsync(isAdmin: true);
        var channelId = "chan_to_ban";

        Db.Channels.Add(Channel.Create(channelId, "badchannel", 2, isArchived: false));
        Db.Subscriptions.AddRange(
            Subscription.Create(regularUser.Id, channelId, DateTime.UtcNow),
            Subscription.Create(adminUser.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("badchannel", "Inappropriate content");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(channelId);

        (await Db.BannedChannels.AnyAsync(b => b.Id == channelId)).Should().BeTrue();
        (await Db.Subscriptions.AnyAsync(uc => uc.UserId == regularUser.Id && uc.ChannelId == channelId)).Should().BeFalse();
        (await Db.Subscriptions.AnyAsync(uc => uc.UserId == adminUser.Id && uc.ChannelId == channelId)).Should().BeTrue();

        // An admin is still subscribed, so the channel stays alive
        (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeTrue();
        await TwitchSubscription.DidNotReceive().RemoveChannelAsync(Arg.Any<Channel>(), Arg.Any<CancellationToken>());
        await RecordingOrchestrator.DidNotReceive().StopRecordingAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task BanChannelAsync_ShouldTearDownChannel_WhenNoAdminIsSubscribed()
    {
        // Arrange
        var regularUser = await CreateUserAsync();
        var channelId = "chan_orphan";
        var channel = Channel.Create(channelId, "orphanchan", 2, isArchived: false);
        channel.SetLive(true);

        Db.Channels.Add(channel);
        Db.Subscriptions.Add(Subscription.Create(regularUser.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("orphanchan", null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.Subscriptions.AnyAsync(uc => uc.ChannelId == channelId)).Should().BeFalse();
        (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeFalse();

        await RecordingOrchestrator.Received(1).StopRecordingAsync(channelId);
        await TwitchSubscription.Received(1).RemoveChannelAsync(
            Arg.Is<Channel>(c => c.Id == channelId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BanChannelAsync_ShouldReturnConflict_WhenAlreadyBanned()
    {
        // Arrange
        Db.BannedChannels.Add(BannedChannel.Create("already_banned", "alreadybanned", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        ChannelExistsOnTwitch("alreadybanned", "already_banned");

        var sut = CreateSut();

        // Act
        var result = await sut.BanChannelAsync("alreadybanned", null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task UnbanChannelAsync_ShouldRemoveChannelFromBannedChannels()
    {
        // Arrange
        Db.BannedChannels.Add(BannedChannel.Create("unban_me", "unbanchan", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnbanChannelAsync("unbanchan");

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.BannedChannels.AnyAsync(b => b.Id == "unban_me")).Should().BeFalse();
    }

    [Fact]
    public async Task UnbanChannelAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        Db.BannedChannels.Add(BannedChannel.Create("unban_me_2", "unbanchan2", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnbanChannelAsync("  UnbanChan2  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.BannedChannels.AnyAsync(b => b.Id == "unban_me_2")).Should().BeFalse();
    }

    [Fact]
    public async Task UnbanChannelAsync_ShouldReturnNotFound_WhenChannelIsNotBanned()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.UnbanChannelAsync("nobodyhere");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnbanChannelAsync_ShouldReturnNotFound_WhenNameIsNotAValidLogin()
    {
        // Arrange
        Db.BannedChannels.Add(BannedChannel.Create("unban_me_3", "unbanchan3", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnbanChannelAsync("not a login");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        (await Db.BannedChannels.AnyAsync(b => b.Id == "unban_me_3")).Should().BeTrue();
    }

    [Fact]
    public async Task ListBannedChannelsAsync_ShouldReturnBannedChannels_NewestFirst()
    {
        // Arrange
        Db.BannedChannels.AddRange(
            BannedChannel.Create("b1", "bannedone", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            BannedChannel.Create("b2", "bannedtwo", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.ListBannedChannelsAsync(Pagination.Default);

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.First().Id.Should().Be("b2");
    }

    [Fact]
    public async Task BanChannelAsync_ShouldMarkStreamsForDeletion_AndPreserveDirectory_WhenStreamsExist()
    {
        // Arrange
        var user = await CreateUserAsync(isAdmin: false);
        var channelId = "ban_with_streams";
        var login = "banstreamchan";
        var channel = Channel.Create(channelId, login, 2, isArchived: false);
        Db.Channels.Add(channel);
        Db.Subscriptions.Add(Subscription.Create(user.Id, channelId, DateTime.UtcNow));

        var streamFolder = StreamFolder.Create(PathsOptions.Value.Streams, channel.Name);
        var stream = Api.Features.Streams.Stream.Create(
            "stream_ban_1",
            channelId,
            streamFolder,
            DateTime.UtcNow,
            "Title",
            "Category");
        Db.Streams.Add(stream);
        await Db.SaveChangesAsync();

        var channelDir = Path.Combine(PathsOptions.Value.Streams, channel.Name);
        var streamDir = streamFolder.AbsolutePath;
        Directory.CreateDirectory(streamDir);
        var urlsFile = streamFolder.RemoteUrlsPath;
        await File.WriteAllTextAsync(urlsFile, "http://remote/ban");

        ChannelExistsOnTwitch(login, channelId);

        try
        {
            var sut = CreateSut();

            // Act
            var result = await sut.BanChannelAsync(login, "Violated TOS");

            // Assert
            result.IsSuccess.Should().BeTrue();
            (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeFalse();

            var updatedStream = await Db.Streams.AsNoTracking().FirstOrDefaultAsync(s => s.Id == "stream_ban_1");
            updatedStream.Should().NotBeNull();
            updatedStream!.StorageOperationStatus.Should().Be(StorageOperationStatus.DeleteRequest);

            Directory.Exists(channelDir).Should().BeTrue();
            File.Exists(urlsFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(channelDir))
                Directory.Delete(channelDir, recursive: true);
        }
    }
}