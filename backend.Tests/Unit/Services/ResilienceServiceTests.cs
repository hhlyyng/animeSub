using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using backend.Services;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for ResilienceService
/// </summary>
public class ResilienceServiceTests
{
    private readonly Mock<ILogger<ResilienceService>> _loggerMock;
    private readonly ResilienceService _sut;

    public ResilienceServiceTests()
    {
        _loggerMock = new Mock<ILogger<ResilienceService>>();
        _sut = new ResilienceService(_loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_SuccessOnFirstAttempt_ReturnsResult()
    {
        // Arrange
        var expectedResult = "success";

        // Act
        var result = await _sut.ExecuteWithRetryAsync(
            _ => Task.FromResult(expectedResult),
            "TestOperation");

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_SuccessAfterRetries_ReturnsResult()
    {
        // Arrange
        var callCount = 0;
        var expectedResult = "success";

        // Act
        var result = await _sut.ExecuteWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount < 3)
                    throw new HttpRequestException("Simulated failure");
                return Task.FromResult(expectedResult);
            },
            "TestOperation");

        // Assert
        result.Should().Be(expectedResult);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAndMetadataAsync_SuccessOnFirstAttempt_ReturnsZeroRetries()
    {
        // Arrange
        var expectedResult = "success";

        // Act
        var (result, retryCount, success) = await _sut.ExecuteWithRetryAndMetadataAsync(
            _ => Task.FromResult(expectedResult),
            "TestOperation");

        // Assert
        result.Should().Be(expectedResult);
        retryCount.Should().Be(0);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithRetryAndMetadataAsync_SuccessAfterRetries_ReturnsCorrectRetryCount()
    {
        // Arrange
        var callCount = 0;
        var expectedResult = "success";

        // Act
        var (result, retryCount, success) = await _sut.ExecuteWithRetryAndMetadataAsync(
            _ =>
            {
                callCount++;
                if (callCount < 2)
                    throw new HttpRequestException("Simulated failure");
                return Task.FromResult(expectedResult);
            },
            "TestOperation");

        // Assert
        result.Should().Be(expectedResult);
        retryCount.Should().Be(1);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithRetryAndMetadataAsync_AllRetriesFail_ReturnsFalseSuccess()
    {
        // Arrange & Act
        var (result, retryCount, success) = await _sut.ExecuteWithRetryAndMetadataAsync<string>(
            _ => throw new HttpRequestException("Simulated failure"),
            "TestOperation");

        // Assert
        success.Should().BeFalse();
        retryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_HandlesTaskCanceledException()
    {
        // Arrange
        var callCount = 0;

        // Act
        var (_, retryCount, success) = await _sut.ExecuteWithRetryAndMetadataAsync<string>(
            _ =>
            {
                callCount++;
                if (callCount <= 3)
                    throw new TaskCanceledException("Timeout");
                return Task.FromResult("success");
            },
            "TestOperation");

        // Assert
        // Should retry on TaskCanceledException
        callCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_HandlesTimeoutException()
    {
        // Arrange
        var callCount = 0;

        // Act
        var (result, retryCount, success) = await _sut.ExecuteWithRetryAndMetadataAsync(
            _ =>
            {
                callCount++;
                if (callCount < 2)
                    throw new TimeoutException("Request timeout");
                return Task.FromResult("success");
            },
            "TestOperation");

        // Assert
        result.Should().Be("success");
        success.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RespectsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act & Assert - The operation should throw when token is checked
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _sut.ExecuteWithRetryAsync(
                async ct =>
                {
                    // Simulate work before checking token
                    await Task.Delay(10, ct);
                    cts.Cancel(); // Cancel during operation
                    ct.ThrowIfCancellationRequested();
                    return "success";
                },
                "TestOperation",
                cts.Token);
        });
    }
}
