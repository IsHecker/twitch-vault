using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TwitchVault.Api.Tests.Unit.Recording;

public class ChapterTrackerTests
{
    private const string ChannelId = "54507525";
    private const string ChannelName = "testchannel";

    private readonly EventBus _eventBus = new(Substitute.For<ILogger<EventBus>>());
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly TestDbContextFactory _factory = new();
    private readonly IDataStore _dataStore;

    private readonly Channel _channel = Channel.Create(ChannelId, ChannelName, 1, isArchived: false);
    private readonly TwitchVault.Api.Features.Streams.Stream _stream;

    public ChapterTrackerTests()
    {
        _dataStore = new EfDataStore(_factory);

        _stream = TwitchVault.Api.Features.Streams.Stream.Create(
            "ts_1", ChannelId, StreamFolder.Create("streams_root", ChannelName),
            new DateTime(2026, 1, 1), "Some Title", "Some Category");

        _dateTimeProvider.DateTimeNow.Returns(new DateTime(2026, 1, 1, 1, 0, 0));
    }

    private ChapterTracker CreateSut() => new(_eventBus, _dataStore, _dateTimeProvider);

    private async Task SeedStreamAsync()
    {
        await using var db = _factory.CreateDbContext();
        if (!db.Channels.Any(c => c.Id == _channel.Id))
            db.Channels.Add(_channel);
        db.Streams.Add(_stream);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MetadataChanged_ShouldAddNewChapter_WhileSessionIsStillActive()
    {
        // Arrange
        await SeedStreamAsync();
        var sut = CreateSut();
        sut.Attach(_stream, _channel);
        var chapterCountBefore = _stream.Chapters.Count;

        // Act
        await _eventBus.PublishAsync(ChannelId, new ChannelUpdateEvent(ChannelId, "New Title", "New Category"));

        // Assert
        _stream.Chapters.Count.Should().Be(chapterCountBefore + 1);
        _stream.CurrentChapter.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task MetadataChanged_ShouldAddChapter_WhenTitleOrCategoryDiffers()
    {
        // Arrange
        await SeedStreamAsync();
        var sut = CreateSut();
        sut.Attach(_stream, _channel);
        var chapterCountBefore = _stream.Chapters.Count;
        var currentCategory = _stream.CurrentChapter.CategoryId;

        // Act — title differs, category unchanged: still splits, since it's an OR check
        await _eventBus.PublishAsync(ChannelId, new ChannelUpdateEvent(ChannelId, "Different Title", currentCategory));

        // Assert
        _stream.Chapters.Count.Should().Be(chapterCountBefore + 1);
    }
}