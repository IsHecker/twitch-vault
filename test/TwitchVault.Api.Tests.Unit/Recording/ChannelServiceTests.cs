using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Persistence.Database;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class ChannelServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AppDbContext _db;
    private readonly ITwitchGqlClient _twitchGqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly ITwitchSubscriptionService _twitchSubscription = Substitute.For<ITwitchSubscriptionService>();
    private readonly IRecordingOrchestrator _recordingOrchestrator = Substitute.For<IRecordingOrchestrator>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();

    public ChannelServiceTests()
    {
        _db = _factory.CreateDbContext();
        _dateTimeProvider.DateTimeNow.Returns(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));
        _pathsOptions.Value.Returns(new PathsOptions { Streams = Path.Combine(Path.GetTempPath(), "TwitchVaultTests") });
        _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxSubscriptionsPerUser = 10 });
    }

    private ChannelService CreateSut() =>
        new(_db, _twitchGqlClient, _twitchSubscription, _recordingOrchestrator, _dateTimeProvider, _pathsOptions, _vaultOptions);

    private async Task<User> CreateUserAsync(Guid? id = null, string? username = null)
    {
        var user = User.Create(
            id ?? Guid.NewGuid(),
            username ?? $"user_{Guid.NewGuid():N}",
            "hashedpwd",
            DateTime.UtcNow);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task AddChannelAsync_ShouldReturnNotFound_WhenChannelNotFoundOnTwitch()
    {
        // Arrange
        _twitchGqlClient.GetChannelIdAsync("unknown_channel", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var sut = CreateSut();

        // Act
        var result = await sut.AddChannelAsync(Guid.NewGuid(), "unknown_channel", null, null, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task AddChannelAsync_ShouldReturnConflict_WhenUserAlreadySubscribed()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channelId = "12345";
        var channelName = "teststreamer";

        _twitchGqlClient.GetChannelIdAsync(channelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(channelId));

        var channel = Channel.Create(channelId, channelName, 2, isArchived: false);
        var userChannel = UserChannel.Create(userId, channelId, DateTime.UtcNow);

        _db.Channels.Add(channel);
        _db.UserChannels.Add(userChannel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.AddChannelAsync(userId, channelName, null, null, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task AddChannelAsync_ShouldReturnValidation_WhenSubscriptionLimitReached()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;

        // Configure limit to 2
        _vaultOptions.CurrentValue.Returns(new VaultOptions { MaxSubscriptionsPerUser = 2 });

        // Add 2 existing subscriptions for the user
        _db.Channels.AddRange(
            Channel.Create("c1", "ch1", 2, false),
            Channel.Create("c2", "ch2", 2, false));
        _db.UserChannels.AddRange(
            UserChannel.Create(userId, "c1", DateTime.UtcNow),
            UserChannel.Create(userId, "c2", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        _twitchGqlClient.GetChannelIdAsync("ch3", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("c3"));

        var sut = CreateSut();

        // Act - attempt to add 3rd channel
        var result = await sut.AddChannelAsync(userId, "ch3", null, null, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("Subscription limit reached");
    }

    [Fact]
    public async Task AddChannelAsync_ShouldLinkUserChannel_WhenChannelAlreadyExistsInDb()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channelId = "12345";
        var channelName = "teststreamer";

        _twitchGqlClient.GetChannelIdAsync(channelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(channelId));

        var channel = Channel.Create(channelId, channelName, 1, isArchived: false);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.AddChannelAsync(userId, channelName, null, null, false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(channelId);

        var userChannel = await _db.UserChannels.FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChannelId == channelId);
        userChannel.Should().NotBeNull();
    }

    [Fact]
    public async Task AddChannelAsync_ShouldCreateNewChannel_WithNonAdminDefaults()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channelId = "67890";
        var channelName = "newstreamer";

        _twitchGqlClient.GetChannelIdAsync(channelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(channelId));

        var sut = CreateSut();

        // Act - non-admin attempts to request quality 0 and archived true
        var result = await sut.AddChannelAsync(userId, channelName, qualityRank: 0, isArchived: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var created = result.Value;
        created.QualityRank.Should().Be(2); // defaulted
        created.IsArchived.Should().BeFalse(); // defaulted

        await _twitchSubscription.Received(1).AddChannelsAsync(
            Arg.Is<ICollection<Channel>>(c => c.Any(ch => ch.Id == channelId)),
            Arg.Any<CancellationToken>());

        await _recordingOrchestrator.Received(1).TryStartRecordingAsync(channelId, channelName);
    }

    [Fact]
    public async Task AddChannelAsync_ShouldRespectAdminOptions_WhenIsAdminIsTrue()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channelId = "99999";
        var channelName = "adminstreamer";

        _twitchGqlClient.GetChannelIdAsync(channelName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(channelId));

        var sut = CreateSut();

        // Act - admin requests quality 0 and archived true
        var result = await sut.AddChannelAsync(userId, channelName, qualityRank: 0, isArchived: true, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var created = result.Value;
        created.QualityRank.Should().Be(0);
        created.IsArchived.Should().BeTrue();

        // When isArchived is true, it should NOT subscribe or start recording
        await _twitchSubscription.DidNotReceive().AddChannelsAsync(Arg.Any<ICollection<Channel>>(), Arg.Any<CancellationToken>());
        await _recordingOrchestrator.DidNotReceive().TryStartRecordingAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldReturnNotFound_WhenUserChannelDoesNotExist()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync(Guid.NewGuid(), "non_existent_channel");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldOnlyRemoveUserSubscription_WhenOtherUsersExist()
    {
        // Arrange
        var user1 = await CreateUserAsync();
        var user2 = await CreateUserAsync();
        var user1Id = user1.Id;
        var user2Id = user2.Id;
        var channelId = "channel_multi";
        var channel = Channel.Create(channelId, "multichannel", 2, isArchived: false);

        _db.Channels.Add(channel);
        _db.UserChannels.Add(UserChannel.Create(user1Id, channelId, DateTime.UtcNow));
        _db.UserChannels.Add(UserChannel.Create(user2Id, channelId, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act - User 1 unsubscribes
        var result = await sut.UnsubscribeChannelAsync(user1Id, channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var user1Subscription = await _db.UserChannels.FirstOrDefaultAsync(uc => uc.UserId == user1Id && uc.ChannelId == channelId);
        user1Subscription.Should().BeNull();

        var user2Subscription = await _db.UserChannels.FirstOrDefaultAsync(uc => uc.UserId == user2Id && uc.ChannelId == channelId);
        user2Subscription.Should().NotBeNull();

        var channelInDb = await _db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        channelInDb.Should().NotBeNull();

        await _twitchSubscription.DidNotReceive().RemoveChannelAsync(Arg.Any<Channel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeChannelAsync_ShouldTearDownChannel_WhenLastUserUnsubscribes()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channelId = "channel_single";
        var channel = Channel.Create(channelId, "singlechannel", 2, isArchived: false);
        channel.SetLive(true);

        _db.Channels.Add(channel);
        _db.UserChannels.Add(UserChannel.Create(userId, channelId, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UnsubscribeChannelAsync(userId, channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var channelInDb = await _db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        channelInDb.Should().BeNull();

        await _recordingOrchestrator.Received(1).StopRecordingAsync(channelId);
        await _twitchSubscription.Received(1).RemoveChannelAsync(Arg.Is<Channel>(c => c.Id == channelId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteChannelAsync_Direct_ShouldReturnNotFound_WhenChannelDoesNotExist()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.DeleteChannelAsync("non_existent_channel");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteChannelAsync_Direct_ShouldDeleteChannelAndCleanup_WhenChannelExists()
    {
        // Arrange
        var channelId = "direct_delete";
        var channel = Channel.Create(channelId, "directchan", 2, isArchived: false);
        channel.SetLive(true);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteChannelAsync(channelId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var channelInDb = await _db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        channelInDb.Should().BeNull();

        await _recordingOrchestrator.Received(1).StopRecordingAsync(channelId);
        await _twitchSubscription.Received(1).RemoveChannelAsync(Arg.Is<Channel>(c => c.Id == channelId), Arg.Any<CancellationToken>());
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
        var channel = Channel.Create("chan_status", "statuschan", 2, isArchived: true);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_status", true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldUnsubscribe_WhenArchivingChannel()
    {
        // Arrange
        var channel = Channel.Create("chan_archive", "archivechan", 2, isArchived: false);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_archive", true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        channel.IsArchived.Should().BeTrue();
        await _twitchSubscription.Received(1).RemoveChannelAsync(Arg.Is<Channel>(c => c.Id == "chan_archive"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetArchiveStatusAsync_ShouldSubscribe_WhenUnarchivingChannel()
    {
        // Arrange
        var channel = Channel.Create("chan_unarchive", "unarchivechan", 2, isArchived: true);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.SetArchiveStatusAsync("chan_unarchive", false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        channel.IsArchived.Should().BeFalse();
        await _twitchSubscription.Received(1).AddChannelsAsync(
            Arg.Is<ICollection<Channel>>(c => c.Any(ch => ch.Id == "chan_unarchive")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateChannelQualityAsync_ShouldUpdateQuality_WhenChannelExists()
    {
        // Arrange
        var channel = Channel.Create("chan_quality", "qualitychan", 2, isArchived: false);
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateChannelQualityAsync("chan_quality", 0);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.QualityRank.Should().Be(0);

        var updated = await _db.Channels.FindAsync("chan_quality");
        updated!.QualityRank.Should().Be(0);
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
    public async Task GetChannelsForUserAsync_ShouldReturnChannels_WhenUserExists()
    {
        // Arrange
        var user = await CreateUserAsync();
        var userId = user.Id;
        var channel1 = Channel.Create("ch1", "chan1", 2, isArchived: false);
        var channel2 = Channel.Create("ch2", "chan2", 1, isArchived: true);

        _db.Channels.AddRange(channel1, channel2);
        _db.UserChannels.AddRange(
            UserChannel.Create(userId, "ch1", DateTime.UtcNow),
            UserChannel.Create(userId, "ch2", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetChannelsForUserAsync(userId, Pagination.Default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Select(c => c.Id).Should().Contain(["ch1", "ch2"]);
    }

    [Fact]
    public async Task GetAllChannelsAsync_ShouldReturnAllChannelsInDatabase()
    {
        // Arrange
        var channel1 = Channel.Create("all1", "allchan1", 2, isArchived: false);
        var channel2 = Channel.Create("all2", "allchan2", 1, isArchived: true);
        _db.Channels.AddRange(channel1, channel2);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var result = await sut.GetAllChannelsAsync(Pagination.Default);

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }
}
