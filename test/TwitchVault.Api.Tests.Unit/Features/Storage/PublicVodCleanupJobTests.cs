using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Features.Settings;
using TwitchVault.Api.Features.Storage.Jobs;
using TwitchVault.Api.Features.Streams;
using TwitchVault.Api.Features.Twitch;
using DomainStream = TwitchVault.Api.Features.Streams.Stream;

namespace TwitchVault.Api.Tests.Unit.Features.Storage;

public class PublicVodCleanupJobTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;
    private readonly ITwitchGqlClient _gqlClient = Substitute.For<ITwitchGqlClient>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly IOptionsMonitor<BackgroundJobsOptions> _jobsOptions = Substitute.For<IOptionsMonitor<BackgroundJobsOptions>>();
    private readonly IOptionsMonitor<VaultOptions> _vaultOptions = Substitute.For<IOptionsMonitor<VaultOptions>>();
    private readonly ILogger<PublicVodCleanupJob> _logger = Substitute.For<ILogger<PublicVodCleanupJob>>();

    private readonly DateTime _now = new(2026, 9, 27, 3, 0, 0, DateTimeKind.Utc);

    public PublicVodCleanupJobTests()
    {
        _dataStore = new EfDataStore(_factory);
        _dateTimeProvider.DateTimeNow.Returns(_now);

        var bgOptions = new BackgroundJobsOptions
        {
            [JobOptions.PublicVodCleanup] = new JobOptions { Enabled = true, CronExpression = "0 0 3 * * ?" }
        };
        _jobsOptions.CurrentValue.Returns(bgOptions);

        _vaultOptions.CurrentValue.Returns(new VaultOptions { PublicVodRetentionDays = 7 });
    }

    private PublicVodCleanupJob CreateSut() =>
        new(_dataStore, _gqlClient, _dateTimeProvider, _jobsOptions, _vaultOptions, _logger);

    private DomainStream SeedStream(string id, string? vodId, DateTime finishedAt, DateTime? vodCheckedAt = null, StorageOperationStatus opStatus = StorageOperationStatus.None)
    {
        using var db = _factory.CreateDbContext();
        var channel = db.Channels.Find("ch-1");
        if (channel == null)
        {
            channel = Channel.Create("ch-1", "testchannel", 1, isArchived: false);
            db.Channels.Add(channel);
        }

        var stream = DomainStream.Create(id, "ch-1", StreamFolder.Create("Streams", "testchannel"), finishedAt.AddHours(-2), "Title", "Cat");
        stream.MarkAsFinished(finishedAt);
        stream.SetVodId(vodId);
        stream.SetStorageOperationStatus(opStatus);
        if (vodCheckedAt.HasValue)
        {
            stream.MarkVodChecked(vodCheckedAt.Value);
        }

        db.Streams.Add(stream);
        db.SaveChanges();
        return stream;
    }

    [Fact]
    public async Task Execute_ShouldDoNothing_WhenJobIsDisabled()
    {
        // Arrange
        var bgOptions = new BackgroundJobsOptions
        {
            [JobOptions.PublicVodCleanup] = new JobOptions { Enabled = false }
        };
        _jobsOptions.CurrentValue.Returns(bgOptions);

        SeedStream("s1", "vod-1", _now.AddDays(-8));

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        await _gqlClient.DidNotReceiveWithAnyArgs().GetVodAccessibilityAsync(default!, default);
    }

    [Fact]
    public async Task Execute_ShouldRequestDeletion_WhenVodIsPublic()
    {
        // Arrange
        SeedStream("s1", "vod-1", _now.AddDays(-8)); // older than 7 days
        _gqlClient.GetVodAccessibilityAsync("vod-1", Arg.Any<CancellationToken>()).Returns(VodAccessibility.Public);

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        using var db = _factory.CreateDbContext();
        var stream = db.Streams.Find("s1")!;
        stream.StorageOperationStatus.Should().Be(StorageOperationStatus.DeleteRequest);
        stream.VodCheckAttemptedAt.Should().Be(_now);
    }

    [Fact]
    public async Task Execute_ShouldKeepStream_WhenVodIsSubscriberOnly()
    {
        // Arrange
        SeedStream("s2", "vod-2", _now.AddDays(-8));
        _gqlClient.GetVodAccessibilityAsync("vod-2", Arg.Any<CancellationToken>()).Returns(VodAccessibility.SubscriberOnly);

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        using var db = _factory.CreateDbContext();
        var stream = db.Streams.Find("s2")!;
        stream.StorageOperationStatus.Should().Be(StorageOperationStatus.None);
        stream.VodCheckAttemptedAt.Should().Be(_now);
    }

    [Fact]
    public async Task Execute_ShouldKeepStream_WhenVodIsNotFound()
    {
        // Arrange
        SeedStream("s3", "vod-3", _now.AddDays(-10));
        _gqlClient.GetVodAccessibilityAsync("vod-3", Arg.Any<CancellationToken>()).Returns(VodAccessibility.NotFound);

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        using var db = _factory.CreateDbContext();
        var stream = db.Streams.Find("s3")!;
        stream.StorageOperationStatus.Should().Be(StorageOperationStatus.None);
        stream.VodCheckAttemptedAt.Should().Be(_now);
    }

    [Fact]
    public async Task Execute_ShouldSkipStream_WhenAlreadyCheckedBefore()
    {
        // Arrange
        SeedStream("s4", "vod-4", _now.AddDays(-8), vodCheckedAt: _now.AddDays(-1));

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        await _gqlClient.DidNotReceiveWithAnyArgs().GetVodAccessibilityAsync(default!, default);
    }

    [Fact]
    public async Task Execute_ShouldSkipStream_WhenWithinRetentionPeriod()
    {
        // Arrange - finished 5 days ago, retention is 7 days
        SeedStream("s5", "vod-5", _now.AddDays(-5));

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        await _gqlClient.DidNotReceiveWithAnyArgs().GetVodAccessibilityAsync(default!, default);
    }

    [Fact]
    public async Task Execute_ShouldSkipStream_WhenVodIdIsNull()
    {
        // Arrange
        SeedStream("s6", null, _now.AddDays(-8));

        var sut = CreateSut();
        var context = Substitute.For<IJobExecutionContext>();

        // Act
        await sut.Execute(context);

        // Assert
        await _gqlClient.DidNotReceiveWithAnyArgs().GetVodAccessibilityAsync(default!, default);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}