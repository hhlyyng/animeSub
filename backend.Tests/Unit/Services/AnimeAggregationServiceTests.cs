using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
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

        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<backend.Data.Entities.AnimeInfoEntity>());

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

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenCacheHit_ReturnsCachedData()
    {
        // Arrange
        var cachedAnimes = TestDataFactory.CreateAnimeInfoDtoList(5);
        var cacheTime = DateTime.UtcNow.AddMinutes(-30);

        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync(cachedAnimes);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync(cacheTime);

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Cache);
        result.IsStale.Should().BeFalse();
        result.Count.Should().Be(5);
        result.Animes.Should().HaveCount(5);

        // Verify API was not called
        _resilienceServiceMock.Verify(
            r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiSuccess_ReturnsApiData()
    {
        // Arrange
        var bangumiResponse = TestDataFactory.CreateBangumiApiResponse(3);
        var tmdbInfo = TestDataFactory.CreateTMDBAnimeInfo();
        var aniListInfo = TestDataFactory.CreateAniListAnimeInfo();

        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync((List<AnimeInfoDto>?)null);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync((DateTime?)null);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((bangumiResponse, 0, true));

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(tmdbInfo);

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(aniListInfo);

        _cacheServiceMock
            .Setup(c => c.GetAnimeImagesCachedAsync(It.IsAny<int>()))
            .ReturnsAsync((backend.Data.Entities.AnimeImagesEntity?)null);

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token", "tmdb-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Api);
        result.IsStale.Should().BeFalse();
        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiFailsWithCache_ReturnsCacheFallback()
    {
        // Arrange
        var cachedAnimes = TestDataFactory.CreateAnimeInfoDtoList(3);
        var cacheTime = DateTime.UtcNow.AddHours(-2);

        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync(cachedAnimes);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync((DateTime?)null); // No fresh cache

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(JsonElement), 3, false));

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.CacheFallback);
        result.IsStale.Should().BeTrue();
        result.RetryAttempts.Should().Be(3);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenApiFailsNoCache_ReturnsFailure()
    {
        // Arrange
        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync((List<AnimeInfoDto>?)null);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync((DateTime?)null);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((default(JsonElement), 3, false));

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        result.Success.Should().BeFalse();
        result.DataSource.Should().Be(DataSource.Api);
        result.IsStale.Should().BeTrue();
        result.Count.Should().Be(0);
        result.RetryAttempts.Should().Be(3);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_ThrowsOnMissingToken()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetTodayAnimeEnrichedAsync(""));
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_SetsTokensOnClients()
    {
        // Arrange
        var cachedAnimes = TestDataFactory.CreateAnimeInfoDtoList(1);
        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync(cachedAnimes);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync(DateTime.UtcNow);

        // Act
        await _sut.GetTodayAnimeEnrichedAsync("bangumi-token", "tmdb-token");

        // Assert
        _bangumiClientMock.Verify(b => b.SetToken("bangumi-token"), Times.Once);
        _tmdbClientMock.Verify(t => t.SetToken("tmdb-token"), Times.Once);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_CachesResultsOnSuccess()
    {
        // Arrange
        var bangumiResponse = TestDataFactory.CreateBangumiApiResponse(2);

        _cacheServiceMock
            .Setup(c => c.GetCachedAnimeListAsync())
            .ReturnsAsync((List<AnimeInfoDto>?)null);
        _cacheServiceMock
            .Setup(c => c.GetTodayScheduleCacheTimeAsync())
            .ReturnsAsync((DateTime?)null);

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((bangumiResponse, 0, true));

        _cacheServiceMock
            .Setup(c => c.GetAnimeImagesCachedAsync(It.IsAny<int>()))
            .ReturnsAsync((backend.Data.Entities.AnimeImagesEntity?)null);

        // Act
        await _sut.GetTodayAnimeEnrichedAsync("test-token");

        // Assert
        _cacheServiceMock.Verify(
            c => c.CacheTodayScheduleAsync(It.IsAny<List<int>>()),
            Times.Once);
        _cacheServiceMock.Verify(
            c => c.CacheAnimeListAsync(It.IsAny<List<AnimeInfoDto>>()),
            Times.Once);
    }
}
