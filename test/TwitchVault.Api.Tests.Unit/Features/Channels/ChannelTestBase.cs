using Microsoft.Extensions.Options;
using NSubstitute;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Tests.Unit.Features.Channels;

public sealed class TestCurrentUser : ICurrentUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsAdmin { get; set; }
}

public abstract class ChannelTestBase : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    protected readonly AppDbContext Db;
    protected readonly ITwitchGqlClient TwitchGqlClient = Substitute.For<ITwitchGqlClient>();
    protected readonly ITwitchSubscriptionService TwitchSubscription = Substitute.For<ITwitchSubscriptionService>();
    protected readonly IRecordingOrchestrator RecordingOrchestrator = Substitute.For<IRecordingOrchestrator>();
    protected readonly IDateTimeProvider DateTimeProvider = Substitute.For<IDateTimeProvider>();
    protected readonly IOptions<PathsOptions> PathsOptions = Substitute.For<IOptions<PathsOptions>>();
    protected readonly IOptionsMonitor<VaultOptions> VaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
    protected readonly TestCurrentUser CurrentUser = new();

    protected ChannelTestBase()
    {
        Db = _factory.CreateDbContext();

        DateTimeProvider.DateTimeNow.Returns(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

        PathsOptions.Value.Returns(new PathsOptions
        {
            Streams = Path.Combine(Path.GetTempPath(), "TwitchVaultTests")
        });

        SetVaultOptions();
    }

    protected void SetVaultOptions(int maxSubscriptionsPerUser = 10, int defaultQualityRank = 2, int maxQualityRank = 3) =>
        VaultOptions.CurrentValue.Returns(new VaultOptions
        {
            MaxSubscriptionsPerUser = maxSubscriptionsPerUser,
            DefaultQualityRank = defaultQualityRank,
            MaxQualityRank = maxQualityRank
        });

    protected void ChannelExistsOnTwitch(string login, string channelId) =>
        TwitchGqlClient.GetChannelIdAsync(login, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(channelId));

    protected async Task<User> CreateUserAsync(bool isAdmin = false, bool asCurrentUser = false)
    {
        var uname = $"user_{Guid.NewGuid():N}";
        var user = User.Create(
            Guid.NewGuid(),
            username: uname,
            email: $"{uname}@example.com",
            googleId: $"google_{Guid.NewGuid():N}",
            createdAt: DateTime.UtcNow,
            isAdmin: isAdmin);

        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        if (asCurrentUser)
        {
            CurrentUser.Id = user.Id;
            CurrentUser.IsAdmin = isAdmin;
        }

        return user;
    }

    protected Task<User> CreateCurrentUserAsync(bool isAdmin = false) => CreateUserAsync(isAdmin, asCurrentUser: true);

    public void Dispose()
    {
        Db.Dispose();
        _factory.Dispose();
    }
}