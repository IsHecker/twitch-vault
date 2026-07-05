using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Tests.Unit.Recording.HLS;

public class SegmentStateTrackerTests
{
    private readonly IOptions<PathsOptions> _pathsOptions = Substitute.For<IOptions<PathsOptions>>();
    private readonly SettingsService _settingsService;
    private readonly SegmentStateTracker _sut;

    public SegmentStateTrackerTests()
    {
        _pathsOptions.Value.Returns(new PathsOptions { Settings = "non_existent_file.json" });
        _settingsService = new SettingsService(_pathsOptions);
        _settingsService.Settings.Vault.MaxSegmentDurationInSec = 30;
        _sut = new SegmentStateTracker(_settingsService);
    }

    [Fact]
    public void GetOrStartSegment_ShouldReturnStartSegment_WhenNoActiveSegmentExists()
    {
        // Act
        var result = _sut.GetOrStartSegment(null, ".ts");

        // Assert
        result.Should().Be("seg_1.ts");
        _sut.CurrentFileName.Should().Be("seg_1.ts");
        _sut.AccumulatedDuration.Should().Be(0f);
        _sut.HasOpenSegment.Should().BeTrue();
    }

    [Fact]
    public void GetOrStartSegment_ShouldIncrementNumber_WhenResumingFromPreviousSegment()
    {
        // Act
        var result = _sut.GetOrStartSegment("seg_14.ts", ".ts");

        // Assert
        result.Should().Be("seg_15.ts");
        _sut.CurrentFileName.Should().Be("seg_15.ts");
        _sut.AccumulatedDuration.Should().Be(0f);
    }

    [Fact]
    public void GetOrStartSegment_ShouldReturnCurrent_WhenSegmentAlreadyOpen()
    {
        // Arrange
        _sut.GetOrStartSegment("seg_1.ts", ".ts");

        // Act
        var result = _sut.GetOrStartSegment("seg_2.ts", ".ts");

        // Assert
        result.Should().Be("seg_2.ts"); // Should return the existing CurrentFileName ("seg_2.ts")
    }

    [Fact]
    public void AddDuration_ShouldAccumulateSeconds_WhenCalled()
    {
        // Arrange
        _sut.GetOrStartSegment(null, ".ts");

        // Act
        _sut.AddDuration(10.5f);
        _sut.AddDuration(5f);

        // Assert
        _sut.AccumulatedDuration.Should().Be(15.5f);
    }

    [Fact]
    public void IsFull_ShouldBeTrue_WhenAccumulatedDurationExceedsMaxSetting()
    {
        // Arrange
        _sut.GetOrStartSegment(null, ".ts");

        // Act
        _sut.AddDuration(35f);

        // Assert
        _sut.IsFull.Should().BeTrue();
    }

    [Fact]
    public void IsFull_ShouldBeFalse_WhenAccumulatedDurationIsLessThanMaxSetting()
    {
        // Arrange
        _sut.GetOrStartSegment(null, ".ts");

        // Act
        _sut.AddDuration(25f);

        // Assert
        _sut.IsFull.Should().BeFalse();
    }

    [Fact]
    public void CloseSegment_ShouldReturnFileNameAndResetState_WhenCalled()
    {
        // Arrange
        _sut.GetOrStartSegment(null, ".ts");
        _sut.AddDuration(20f);

        // Act
        var (fileName, duration) = _sut.CloseSegment();

        // Assert
        fileName.Should().Be("seg_1.ts");
        duration.Should().Be(20f);
        _sut.CurrentFileName.Should().BeNull();
        _sut.AccumulatedDuration.Should().Be(0f);
        _sut.HasOpenSegment.Should().BeFalse();
    }
}