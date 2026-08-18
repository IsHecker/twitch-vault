using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Tests.Unit.Common;

public class TransientErrorRetryPolicyTests
{
    private readonly ILogger<TransientErrorRetryPolicy> _logger = Substitute.For<ILogger<TransientErrorRetryPolicy>>();

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResult_WhenActionSucceedsOnFirstAttempt()
    {
        // Arrange
        var policy = new TransientErrorRetryPolicy(3, TimeSpan.Zero, _logger);
        var attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult("success");
        }, CancellationToken.None);

        // Assert
        result.Should().Be("success");
        attempts.Should().Be(1);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    public async Task ExecuteAsync_ShouldRetryAndSucceed_WhenTransientErrorIsFollowedBySuccess(Type exceptionType)
    {
        // Arrange
        var policy = new TransientErrorRetryPolicy(3, TimeSpan.Zero, _logger);
        var attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw (Exception)Activator.CreateInstance(exceptionType, "Transient error")!;
            }
            return Task.FromResult("recovered");
        }, CancellationToken.None);

        // Assert
        result.Should().Be("recovered");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowException_WhenTransientErrorsExceedMaxAttempts()
    {
        // Arrange
        const int maxAttempts = 3;
        var policy = new TransientErrorRetryPolicy(maxAttempts, TimeSpan.Zero, _logger);
        var attempts = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new HttpRequestException("Network failure");
        }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Network failure");
        attempts.Should().Be(maxAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowImmediately_WhenNonTransientExceptionOccurs()
    {
        // Arrange
        var policy = new TransientErrorRetryPolicy(5, TimeSpan.Zero, _logger);
        var attempts = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new InvalidOperationException("Non-transient error");
        }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Non-transient error");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowImmediately_WhenOperationCanceledExceptionOccurs()
    {
        // Arrange
        var policy = new TransientErrorRetryPolicy(5, TimeSpan.Zero, _logger);
        var attempts = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new OperationCanceledException("Operation was canceled");
        }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>()
            .WithMessage("Operation was canceled");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogWarning_OnEachTransientRetry()
    {
        // Arrange
        var delay = TimeSpan.FromMilliseconds(1);
        var policy = new TransientErrorRetryPolicy(3, delay, _logger);
        var attempts = 0;

        // Act
        await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException($"Attempt {attempts} failed");
            }
            return Task.FromResult("ok");
        }, CancellationToken.None);

        // Assert
        _logger.ReceivedCalls().Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotThrowNullReference_WhenLoggerIsNull()
    {
        // Arrange
        var policy = new TransientErrorRetryPolicy(2, TimeSpan.Zero, null!);
        var attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts == 1)
                throw new HttpRequestException("Temporary blip");

            return Task.FromResult(42);
        }, CancellationToken.None);

        // Assert
        result.Should().Be(42);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelDelay_WhenCancellationTokenIsCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var policy = new TransientErrorRetryPolicy(3, TimeSpan.FromSeconds(10), _logger);
        var attempts = 0;

        // Act
        var act = async () => await policy.ExecuteAsync<string>(() =>
        {
            attempts++;
            cts.Cancel(); // Cancel on the first failure before delay completes
            throw new HttpRequestException("Blip");
        }, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }
}