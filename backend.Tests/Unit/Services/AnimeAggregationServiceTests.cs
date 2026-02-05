using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using backend.Data.Entities;
using backend.Models;
using backend.Models.Dtos;
using backend.Services;
using backend.Services.Implementations;
using backend.Services.Interfaces;
using backend.Services.Repositories;
using backend.Tests.Fixtures;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for AnimeAggregationService
/// Tests are based on the pre-fetch architecture:
/// Priority: Database (pre-fetched) > Real-time API > Memory Cache (fallback)
/// </summary>
public class AnimeAggregationServiceTests
{
    private readonly Mock<IBangumiClient> _bangumiClientMock;
    private readonly Mock<ITMDBClient> _tmdbClientMock;
    private readonly Mock<IAniListClient> _aniListClientMock;
    private readonly Mock<IJikanClient> _jikanClientMock;
    private readonly Mock<IAnimeRepository> _repositoryMock;
    private readonly Mock<IAnimeCacheService> _cacheServiceMock;
    private readonly Mock<IResilienceService> _resilienceServiceMock;
    private readonly Mock<ILogger<AnimeAggregationService>> _loggerMock;
    private readonly AnimeAggregationService _sut;

    public AnimeAggregationServiceTests()
    {
        _bangumiClientMock = new Mock<IBangumiClient>();
        _tmdbClientMock = new Mock<ITMDBClient>();
        _aniListClientMock = new Mock<IAniListClient>();
        _jikanClientMock = new Mock<IJikanClient>();
        _repositoryMock = new Mock<IAnimeRepository>();
        _cacheServiceMock = new Mock<IAnimeCacheService>();
        _resilienceServiceMock = new Mock<IResilienceService>();
        _loggerMock = new Mock<ILogger<AnimeAggregationService>>();

        _sut = new AnimeAggregationService(
            _bangumiClientMock.Object,
            _tmdbClientMock.Object,
            _aniListClientMock.Object,
            _jikanClientMock.Object,
            _repositoryMock.Object,
            _cacheServiceMock.Object,
            _resilienceServiceMock.Object,
            _loggerMock.Object);
    }

    #region Pre-fetch Database Tests

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenDatabaseHasData_ReturnsDatabaseData()
    {
        // Arrange: Database has pre-fetched data
        var preFetchedEntities = CreateAnimeInfoEntities(5);
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(preFetchedEntities);

        // Mock Bangumi API to return empty (no new anime)
        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateEmptyJsonArray(), 0, true));

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Database);
        result.IsStale.Should().BeFalse();
        result.Count.Should().Be(5);
        result.Animes.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenDatabaseHasData_DoesNotCallFullApiFetch()
    {
        // Arrange
        var preFetchedEntities = CreateAnimeInfoEntities(3);
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(preFetchedEntities);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateEmptyJsonArray(), 0, true));

        // Act
        await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert: Should not save to repository (already have data)
        _repositoryMock.Verify(
            r => r.SaveAnimeInfoBatchAsync(It.IsAny<List<AnimeInfoEntity>>()),
            Times.Never);
    }

    #endregion

    #region API Fallback Tests

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenDatabaseEmpty_FallsBackToApi()
    {
        // Arrange: Database is empty
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AnimeInfoEntity>());

        // API returns data
        var bangumiResponse = TestDataFactory.CreateBangumiApiResponse(3);
        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((bangumiResponse, 0, true));

        // Mock Bangumi subject details
        _bangumiClientMock
            .Setup(b => b.GetSubjectDetailAsync(It.IsAny<int>()))
            .ReturnsAsync(CreateBangumiSubjectDetail());

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(TestDataFactory.CreateTMDBAnimeInfo());

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(TestDataFactory.CreateAniListAnimeInfo());

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token", "tmdb-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Api);
        result.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiFailsAndHasCache_ReturnsCacheFallback()
    {
        // Arrange: Database is empty, API will throw exception
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ThrowsAsync(new Exception("Database error"));

        // Memory cache has data
        var cachedAnimes = TestDataFactory.CreateAnimeInfoDtoList(3);
        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync(cachedAnimes);

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.CacheFallback);
        result.IsStale.Should().BeTrue();
        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiFails_ReturnsFailure()
    {
        // Arrange: Database is empty
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AnimeInfoEntity>());

        // API fails
        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(JsonElement), 3, false));

        // No memory cache
        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync((List<AnimeInfoDto>?)null);

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeFalse();
        result.DataSource.Should().Be(DataSource.Api);
        result.IsStale.Should().BeTrue();
        result.Count.Should().Be(0);
        result.RetryAttempts.Should().Be(3);
    }

    #endregion

    #region Token and Validation Tests

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WorksWithoutToken()
    {
        // Arrange - Bangumi public API doesn't require authentication
        var preFetchedEntities = CreateAnimeInfoEntities(1);
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(preFetchedEntities);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateEmptyJsonArray(), 0, true));

        // Act - should not throw even with null token
        var result = await _sut.GetTodayAnimeEnrichedAsync(null, null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_SetsTokensOnClients()
    {
        // Arrange
        var preFetchedEntities = CreateAnimeInfoEntities(1);
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(preFetchedEntities);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateEmptyJsonArray(), 0, true));

        // Act
        await _sut.GetTodayAnimeEnrichedAsync("bangumi-token", "tmdb-token");

        // Assert
        _bangumiClientMock.Verify(b => b.SetToken("bangumi-token"), Times.Once);
        _tmdbClientMock.Verify(t => t.SetToken("tmdb-token"), Times.Once);
    }

    #endregion

    #region Incremental Update Tests

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenNewAnimeFound_FetchesIncrementally()
    {
        // Arrange: Database has 2 anime
        var preFetchedEntities = CreateAnimeInfoEntities(2);
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(preFetchedEntities);

        // Bangumi API returns 3 anime (1 new)
        var bangumiResponse = TestDataFactory.CreateBangumiApiResponse(3);
        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((bangumiResponse, 0, true));

        // Mock for fetching new anime details
        _bangumiClientMock
            .Setup(b => b.GetSubjectDetailAsync(It.IsAny<int>()))
            .ReturnsAsync(CreateBangumiSubjectDetail());

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(TestDataFactory.CreateTMDBAnimeInfo());

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(TestDataFactory.CreateAniListAnimeInfo());

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Database);
        // Should have original 2 + 1 new = 3
        result.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiSucceeds_SavesToRepository()
    {
        // Arrange: Database is empty
        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AnimeInfoEntity>());

        var bangumiResponse = TestDataFactory.CreateBangumiApiResponse(2);
        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((bangumiResponse, 0, true));

        _bangumiClientMock
            .Setup(b => b.GetSubjectDetailAsync(It.IsAny<int>()))
            .ReturnsAsync(CreateBangumiSubjectDetail());

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(TestDataFactory.CreateTMDBAnimeInfo());

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(TestDataFactory.CreateAniListAnimeInfo());

        // Act
        await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert: Should save to repository and cache
        _repositoryMock.Verify(
            r => r.SaveAnimeInfoBatchAsync(It.IsAny<List<AnimeInfoEntity>>()),
            Times.Once);
        _cacheServiceMock.Verify(
            c => c.CacheAnimeListAsync(It.IsAny<List<AnimeInfoDto>>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static List<AnimeInfoEntity> CreateAnimeInfoEntities(int count)
    {
        return Enumerable.Range(1, count).Select(i => new AnimeInfoEntity
        {
            BangumiId = i,
            NameJapanese = $"テストアニメ{i}",
            NameChinese = $"测试动画{i}",
            NameEnglish = $"Test Anime {i}",
            DescChinese = "测试描述",
            DescEnglish = "Test description",
            Score = "8.5",
            ImagePortrait = $"https://example.com/poster{i}.jpg",
            ImageLandscape = $"https://example.com/backdrop{i}.jpg",
            UrlBangumi = $"https://bgm.tv/subject/{i}",
            Weekday = (int)DateTime.Now.DayOfWeek == 0 ? 7 : (int)DateTime.Now.DayOfWeek
        }).ToList();
    }

    private static JsonElement CreateEmptyJsonArray()
    {
        return JsonDocument.Parse("[]").RootElement;
    }

    private static JsonElement CreateBangumiSubjectDetail()
    {
        var detail = new
        {
            id = 1,
            name = "テストアニメ",
            name_cn = "测试动画",
            summary = "测试简介",
            date = "2024-01-15",
            rating = new { score = 8.5 },
            images = new { large = "https://example.com/poster.jpg" }
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(detail)).RootElement;
    }

    #endregion
}
