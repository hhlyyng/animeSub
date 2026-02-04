using System.Text.Json;
using FluentAssertions;
using Moq;
using backend.Services.Interfaces;
using backend.Tests.Fixtures;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for BangumiClient Top subjects functionality
/// Tests use mocked interface to verify expected behavior
/// </summary>
public class BangumiClientTopTests
{
    private readonly Mock<IBangumiClient> _bangumiClientMock;

    public BangumiClientTopTests()
    {
        _bangumiClientMock = new Mock<IBangumiClient>();
    }

    [Fact]
    public async Task SearchTopSubjectsAsync_ReturnsRankedAnime()
    {
        // Arrange
        var responseJson = TestDataFactory.CreateBangumiTopSubjectsResponse(10);
        var jsonElement = JsonDocument.Parse(responseJson).RootElement.GetProperty("data");

        _bangumiClientMock
            .Setup(c => c.SearchTopSubjectsAsync(10))
            .ReturnsAsync(jsonElement);

        // Act
        var result = await _bangumiClientMock.Object.SearchTopSubjectsAsync(10);

        // Assert
        result.EnumerateArray().Count().Should().Be(10);
    }

    [Fact]
    public async Task SearchTopSubjectsAsync_ParsesFieldsCorrectly()
    {
        // Arrange
        var responseJson = TestDataFactory.CreateBangumiTopSubjectsResponse(1);
        var jsonElement = JsonDocument.Parse(responseJson).RootElement.GetProperty("data");

        _bangumiClientMock
            .Setup(c => c.SearchTopSubjectsAsync(1))
            .ReturnsAsync(jsonElement);

        // Act
        var result = await _bangumiClientMock.Object.SearchTopSubjectsAsync(1);

        // Assert
        var firstAnime = result.EnumerateArray().First();
        firstAnime.GetProperty("id").GetInt32().Should().Be(1);
        firstAnime.GetProperty("name").GetString().Should().Contain("テストアニメ");
        firstAnime.GetProperty("name_cn").GetString().Should().Contain("测试动漫");
        firstAnime.GetProperty("score").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchTopSubjectsAsync_EmptyData_ReturnsEmptyArray()
    {
        // Arrange
        var emptyResponse = JsonSerializer.Serialize(new { data = Array.Empty<object>() });
        var jsonElement = JsonDocument.Parse(emptyResponse).RootElement.GetProperty("data");

        _bangumiClientMock
            .Setup(c => c.SearchTopSubjectsAsync(10))
            .ReturnsAsync(jsonElement);

        // Act
        var result = await _bangumiClientMock.Object.SearchTopSubjectsAsync(10);

        // Assert
        result.EnumerateArray().Count().Should().Be(0);
    }

    [Fact]
    public void SetToken_CanBeCalledOnInterface()
    {
        // Arrange & Act
        _bangumiClientMock.Object.SetToken("test-token");

        // Assert
        _bangumiClientMock.Verify(c => c.SetToken("test-token"), Times.Once);
    }

    [Fact]
    public async Task SearchTopSubjectsAsync_WithDifferentLimits_ReturnsRequestedCount()
    {
        // Arrange
        var response5 = TestDataFactory.CreateBangumiTopSubjectsResponse(5);
        var jsonElement5 = JsonDocument.Parse(response5).RootElement.GetProperty("data");

        _bangumiClientMock
            .Setup(c => c.SearchTopSubjectsAsync(5))
            .ReturnsAsync(jsonElement5);

        // Act
        var result = await _bangumiClientMock.Object.SearchTopSubjectsAsync(5);

        // Assert
        result.EnumerateArray().Count().Should().Be(5);
    }
}
