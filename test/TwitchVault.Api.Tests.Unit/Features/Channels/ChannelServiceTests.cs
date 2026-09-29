using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace TwitchVault.Api.Tests.Unit.Features.Channels;

public class ChannelServiceTests : ChannelTestBase
{
    private ChannelService CreateSut() =>
        new(Db, TwitchGqlClient, TwitchSubscription, RecordingOrchestrator, DateTimeProvider, PathsOptions, CurrentUser, VaultOptions);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("bad name")]
    [InlineData("_leadingunderscore")]
    [InlineData("../../etc/passwd")]
    public async Task SubscribeToChannelAsync_ShouldReturnValidation_WhenChannelNameIsNotAValidTwitchLogin(string channelName)
    {
        // Arrange
        await CreateCurrentUserAsync();
        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync(channelName, null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await TwitchGqlClient.DidNotReceive().GetChannelIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldNormalizeChannelName_BeforeLookupAndPersistence()
    {
        // Arrange
        await CreateCurrentUserAsync();
        ChannelExistsOnTwitch("teststreamer", "12345");
        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("  TestStreamer  ", null, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("teststreamer");
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldReturnNotFound_WhenChannelNotFoundOnTwitch()
    {
        // Arrange
        await CreateCurrentUserAsync();
        TwitchGqlClient.GetChannelIdAsync("unknown_channel", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("unknown_channel", null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldReturnConflict_WhenUserAlreadySubscribed()
    {
        // Arrange
        var user = await CreateCurrentUserAsync();
        ChannelExistsOnTwitch("teststreamer", "12345");

        Db.Channels.Add(Channel.Create("12345", "teststreamer", 2, isArchived: false));
        Db.Subscriptions.Add(Subscription.Create(user.Id, "12345", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("teststreamer", null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldReturnValidation_WhenSubscriptionLimitReached()
    {
        // Arrange
        var user = await CreateCurrentUserAsync();
        SetVaultOptions(maxSubscriptionsPerUser: 2);

        Db.Channels.AddRange(
            Channel.Create("c1", "ch1", 2, false),
            Channel.Create("c2", "ch2", 2, false));
        Db.Subscriptions.AddRange(
            Subscription.Create(user.Id, "c1", DateTime.UtcNow),
            Subscription.Create(user.Id, "c2", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        ChannelExistsOnTwitch("ch3", "c3");
        var sut = CreateSut();

        // Act - attempt to subscribe to a 3rd channel
        var result = await sut.SubscribeToChannelAsync("ch3", null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("Subscription limit reached");
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldIgnoreSubscriptionLimit_WhenUserIsAdmin()
    {
        // Arrange
        var admin = await CreateCurrentUserAsync(isAdmin: true);
        SetVaultOptions(maxSubscriptionsPerUser: 2);

        Db.Channels.AddRange(
            Channel.Create("c1", "ch1", 2, false),
            Channel.Create("c2", "ch2", 2, false));
        Db.Subscriptions.AddRange(
            Subscription.Create(admin.Id, "c1", DateTime.UtcNow),
            Subscription.Create(admin.Id, "c2", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        ChannelExistsOnTwitch("ch3", "c3");
        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("ch3", null, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.Subscriptions.CountAsync(uc => uc.UserId == admin.Id)).Should().Be(3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task SubscribeToChannelAsync_ShouldReturnValidation_WhenAdminRequestsQualityRankOutOfRange(int qualityRank)
    {
        // Arrange
        await CreateCurrentUserAsync(isAdmin: true);
        ChannelExistsOnTwitch("teststreamer", "12345");
        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("teststreamer", qualityRank, isArchived: null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await TwitchGqlClient.DidNotReceive().GetChannelIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldLinkSubscription_WhenChannelAlreadyExistsInDb()
    {
        // Arrange
        var user = await CreateCurrentUserAsync();
        ChannelExistsOnTwitch("teststreamer", "12345");

        Db.Channels.Add(Channel.Create("12345", "teststreamer", 1, isArchived: false));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("teststreamer", null, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("12345");

        var subscriptions = await Db.Subscriptions
            .FirstOrDefaultAsync(uc => uc.UserId == user.Id && uc.ChannelId == "12345");
        subscriptions.Should().NotBeNull();

        // Existing channel: no new subscription or recording should be kicked off
        await TwitchSubscription.DidNotReceive().AddChannelsAsync(Arg.Any<ICollection<Channel>>(), Arg.Any<CancellationToken>());
        await RecordingOrchestrator.DidNotReceive().TryStartRecordingAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldCreateNewChannel_WithNonAdminDefaults()
    {
        // Arrange
        await CreateCurrentUserAsync();
        ChannelExistsOnTwitch("newstreamer", "67890");
        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("newstreamer", qualityRank: 0, isArchived: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.QualityRank.Should().Be(2);    // DefaultQualityRank from options
        result.Value.IsArchived.Should().BeFalse(); // defaulted

        await TwitchSubscription.Received(1).AddChannelsAsync(
            Arg.Is<ICollection<Channel>>(c => c.Any(ch => ch.Id == "67890")),
            Arg.Any<CancellationToken>());

        await RecordingOrchestrator.Received(1).TryStartRecordingAsync("67890", "newstreamer");
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldRespectAdminOptions_WhenCurrentUserIsAdmin()
    {
        // Arrange
        await CreateCurrentUserAsync(isAdmin: true);
        ChannelExistsOnTwitch("adminstreamer", "99999");
        var sut = CreateSut();

        // Act - admin requests quality 0 and archived true
        var result = await sut.SubscribeToChannelAsync("adminstreamer", qualityRank: 0, isArchived: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.QualityRank.Should().Be(0);
        result.Value.IsArchived.Should().BeTrue();

        // Archived channels are neither subscribed nor recorded
        await TwitchSubscription.DidNotReceive().AddChannelsAsync(Arg.Any<ICollection<Channel>>(), Arg.Any<CancellationToken>());
        await RecordingOrchestrator.DidNotReceive().TryStartRecordingAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldReturnValidation_WhenChannelIsBannedAndUserIsNotAdmin()
    {
        // Arrange
        await CreateCurrentUserAsync();
        ChannelExistsOnTwitch("bannedchannel", "banned_123");

        Db.BannedChannels.Add(BannedChannel.Create("banned_123", "bannedchannel", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("bannedchannel", null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task SubscribeToChannelAsync_ShouldSucceed_WhenChannelIsBannedAndUserIsAdmin()
    {
        // Arrange
        await CreateCurrentUserAsync(isAdmin: true);
        ChannelExistsOnTwitch("adminallowedchannel", "banned_456");

        Db.BannedChannels.Add(BannedChannel.Create("banned_456", "adminallowedchannel", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SubscribeToChannelAsync("adminallowedchannel", null, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("banned_456");
    }


    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldReturnNotFound_WhenSubscriptionDoesNotExist()
    {
        // Arrange
        await CreateCurrentUserAsync();
        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync("non_existent_channel");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldOnlyRemoveUserSubscription_WhenOtherUsersExist()
    {
        // Arrange
        var user1 = await CreateCurrentUserAsync();
        var user2 = await CreateUserAsync();
        var channelId = "channel_multi";

        Db.Channels.Add(Channel.Create(channelId, "multichannel", 2, isArchived: false));
        Db.Subscriptions.AddRange(
            Subscription.Create(user1.Id, channelId, DateTime.UtcNow),
            Subscription.Create(user2.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act - user 1 unsubscribes
        var result = await sut.UnsubscribeChannelAsync(channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.Subscriptions.AnyAsync(uc => uc.UserId == user1.Id && uc.ChannelId == channelId)).Should().BeFalse();
        (await Db.Subscriptions.AnyAsync(uc => uc.UserId == user2.Id && uc.ChannelId == channelId)).Should().BeTrue();
        (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeTrue();

        await TwitchSubscription.DidNotReceive().RemoveChannelAsync(Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldKeepChannel_WhenSeveralOtherUsersExist()
    {
        // Arrange - guards the off-by-one the old `remainingUserCount - 1` check depended on
        var user1 = await CreateCurrentUserAsync();
        var user2 = await CreateUserAsync();
        var user3 = await CreateUserAsync();
        var channelId = "channel_crowd";

        Db.Channels.Add(Channel.Create(channelId, "crowdchannel", 2, isArchived: false));
        Db.Subscriptions.AddRange(
            Subscription.Create(user1.Id, channelId, DateTime.UtcNow),
            Subscription.Create(user2.Id, channelId, DateTime.UtcNow),
            Subscription.Create(user3.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync(channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.Subscriptions.CountAsync(uc => uc.ChannelId == channelId)).Should().Be(2);
        (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldTearDownChannel_WhenLastUserUnsubscribes()
    {
        // Arrange
        var user = await CreateCurrentUserAsync();
        var channelId = "channel_single";
        var channel = Channel.Create(channelId, "singlechannel", 2, isArchived: false);
        channel.SetLive(true);

        Db.Channels.Add(channel);
        Db.Subscriptions.Add(Subscription.Create(user.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync(channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        (await Db.Channels.AnyAsync(c => c.Id == channelId)).Should().BeFalse();

        await RecordingOrchestrator.Received(1).StopRecordingAsync(channelId);
        await TwitchSubscription.Received(1).RemoveChannelAsync(
            Arg.Is<Channel>(c => c.Id == channelId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldNotStopRecording_WhenChannelIsNotLive()
    {
        // Arrange
        var user = await CreateCurrentUserAsync();
        var channelId = "channel_offline";

        Db.Channels.Add(Channel.Create(channelId, "offlinechan", 2, isArchived: false));
        Db.Subscriptions.Add(Subscription.Create(user.Id, channelId, DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync(channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await RecordingOrchestrator.DidNotReceive().StopRecordingAsync(Arg.Any<string>());
        await TwitchSubscription.Received(1).RemoveChannelAsync(
            Arg.Is<Channel>(c => c.Id == channelId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldReturnNotFound_WhenChannelDoesNotExist()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("non_existent", true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldReturnValidation_WhenStatusAlreadyMatches()
    {
        // Arrange
        Db.Channels.Add(Channel.Create("chan_status", "statuschan", 2, isArchived: true));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_status", true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await TwitchSubscription.DidNotReceive().RemoveChannelAsync(Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldUnsubscribe_WhenArchivingChannel()
    {
        // Arrange
        var channel = Channel.Create("chan_archive", "archivechan", 2, isArchived: false);
        Db.Channels.Add(channel);
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_archive", true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        channel.IsArchived.Should().BeTrue();
        await TwitchSubscription.Received(1).RemoveChannelAsync(
            Arg.Is<Channel>(c => c.Id == "chan_archive"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldSubscribe_WhenUnarchivingChannel()
    {
        // Arrange
        var channel = Channel.Create("chan_unarchive", "unarchivechan", 2, isArchived: true);
        Db.Channels.Add(channel);
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_unarchive", false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        channel.IsArchived.Should().BeFalse();
        await TwitchSubscription.Received(1).AddChannelsAsync(
            Arg.Is<ICollection<Channel>>(c => c.Any(ch => ch.Id == "chan_unarchive")),
            Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task UpdateChannelQualityAsync_ShouldUpdateQuality_WhenChannelExists()
    {
        // Arrange
        Db.Channels.Add(Channel.Create("chan_quality", "qualitychan", 2, isArchived: false));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.ChangeChannelQualityAsync("chan_quality", 0);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await Db.Channels.FindAsync("chan_quality");
        updated!.QualityRank.Should().Be(0);
    }

    [Fact]
    public async Task UpdateChannelQualityAsync_ShouldReturnNotFound_WhenChannelDoesNotExist()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.ChangeChannelQualityAsync("non_existent", 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task UpdateChannelQualityAsync_ShouldReturnValidation_WhenQualityRankOutOfRange(int qualityRank)
    {
        // Arrange
        Db.Channels.Add(Channel.Create("chan_quality_bad", "qualitybad", 2, isArchived: false));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.ChangeChannelQualityAsync("chan_quality_bad", qualityRank);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);

        var unchanged = await Db.Channels.FindAsync("chan_quality_bad");
        unchanged!.QualityRank.Should().Be(2);
    }

    [Fact]
    public async Task GetAllChannelsAsync_ShouldReturnAllChannelsInDatabase()
    {
        // Arrange
        Db.Channels.AddRange(
            Channel.Create("all1", "allchan1", 2, isArchived: false),
            Channel.Create("all2", "allchan2", 1, isArchived: true));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetAllChannelsAsync(Pagination.Default);

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetChannelsForUserAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetChannelsForUserAsync(Guid.NewGuid(), Pagination.Default);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetChannelsForUserAsync_ShouldReturnSubscribedChannels_WhenUserExists()
    {
        // Arrange
        var user = await CreateUserAsync();
        Db.Channels.AddRange(
            Channel.Create("ch1", "chan1", 2, isArchived: false),
            Channel.Create("ch2", "chan2", 1, isArchived: true));
        Db.Subscriptions.AddRange(
            Subscription.Create(user.Id, "ch1", DateTime.UtcNow),
            Subscription.Create(user.Id, "ch2", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetChannelsForUserAsync(user.Id, Pagination.Default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Select(c => c.Id).Should().Contain(["ch1", "ch2"]);
    }

    [Fact]
    public async Task GetChannelsForUserAsync_ShouldExcludeBannedChannels_WhenUserIsNotAdmin()
    {
        // Arrange
        var user = await CreateUserAsync(isAdmin: false);
        Db.Channels.AddRange(
            Channel.Create("ok_chan", "okchan", 2, isArchived: false),
            Channel.Create("banned_chan", "bannedchan", 2, isArchived: false));
        Db.BannedChannels.Add(BannedChannel.Create("banned_chan", "bannedchan", DateTime.UtcNow));
        Db.Subscriptions.AddRange(
            Subscription.Create(user.Id, "ok_chan", DateTime.UtcNow),
            Subscription.Create(user.Id, "banned_chan", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetChannelsForUserAsync(user.Id, Pagination.Default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items.Single().Id.Should().Be("ok_chan");
    }

    [Fact]
    public async Task GetChannelsForUserAsync_ShouldIncludeBannedChannels_WhenUserIsAdmin()
    {
        // Arrange
        var admin = await CreateUserAsync(isAdmin: true);
        Db.Channels.AddRange(
            Channel.Create("ok_chan_admin", "okchanadmin", 2, isArchived: false),
            Channel.Create("banned_chan_admin", "bannedchanadmin", 2, isArchived: false));
        Db.BannedChannels.Add(BannedChannel.Create("banned_chan_admin", "bannedchanadmin", DateTime.UtcNow));
        Db.Subscriptions.AddRange(
            Subscription.Create(admin.Id, "ok_chan_admin", DateTime.UtcNow),
            Subscription.Create(admin.Id, "banned_chan_admin", DateTime.UtcNow));
        await Db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetChannelsForUserAsync(admin.Id, Pagination.Default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
    }
}