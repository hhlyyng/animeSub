using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services;
using backend.Services.Repositories;
using backend.Tests.Fixtures;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for AnimeCacheService
/// </summary>
public class AnimeCacheServiceTests
{
    private readonly Mock<IAnimeRepository> _repositoryMock;
    private readonly Mock<ILogger<AnimeCacheService>> _loggerMock;
    private readonly IMemoryCache _memoryCache;
    private readonly AnimeCacheService _sut;

    public AnimeCacheServiceTests()
    {
        _repositoryMock = new Mock<IAnimeRepository>();
        _loggerMock = new Mock<ILogger<AnimeCacheService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _sut = new AnimeCacheService(_memoryCache, _repositoryMock.Object, _loggerMock.Object);
    }

    #region Daily Schedule Tests

    [Fact]
    public async Task GetTodayScheduleCachedAsync_WhenInMemoryCache_ReturnsFromMemory()
    {
        // Arrange
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var cacheKey = $"daily_schedule_{today}";
        var expectedIds = new List<int> { 1, 2, 3 };
        _memoryCache.Set(cacheKey, expectedIds);

        // Act
        var result = await _sut.GetTodayScheduleCachedAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedIds);
        _repositoryMock.Verify(r => r.GetDailyScheduleAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetTodayScheduleCachedAsync_WhenNotInMemory_FetchesFromSqlite()
    {
        // Arrange
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var expectedIds = new List<int> { 1, 2, 3 };
        _repositoryMock
            .Setup(r => r.GetDailyScheduleAsync(today))
            .ReturnsAsync(expectedIds);

        // Act
        var result = await _sut.GetTodayScheduleCachedAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedIds);
        _repositoryMock.Verify(r => r.GetDailyScheduleAsync(today), Times.Once);
    }

    [Fact]
    public async Task GetTodayScheduleCachedAsync_WhenNotCached_ReturnsNull()
    {
        // Arrange
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        _repositoryMock
            .Setup(r => r.GetDailyScheduleAsync(today))
            .ReturnsAsync((List<int>?)null);

        // Act
        var result = await _sut.GetTodayScheduleCachedAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CacheTodayScheduleAsync_SavesToBothCaches()
    {
        // Arrange
        var ids = new List<int> { 1, 2, 3 };
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // Act
        await _sut.CacheTodayScheduleAsync(ids);

        // Assert
        _repositoryMock.Verify(r => r.SaveDailyScheduleAsync(today, ids), Times.Once);

        // Verify memory cache was populated
        var result = await _sut.GetTodayScheduleCachedAsync();
        result.Should().BeEquivalentTo(ids);
    }

    #endregion

    #region Anime List Tests

    [Fact]
    public async Task GetCachedAnimeListAsync_WhenInMemoryCache_ReturnsList()
    {
        // Arrange
        var animeList = TestDataFactory.CreateAnimeInfoDtoList(3);
        _memoryCache.Set("anime_list_today", animeList);

        // Act
        var result = await _sut.GetCachedAnimeListAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetCachedAnimeListAsync_WhenNotCached_ReturnsNull()
    {
        // Act
        var result = await _sut.GetCachedAnimeListAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CacheAnimeListAsync_StoresInMemoryCache()
    {
        // Arrange
        var animeList = TestDataFactory.CreateAnimeInfoDtoList(5);

        // Act
        await _sut.CacheAnimeListAsync(animeList);

        // Assert
        var result = await _sut.GetCachedAnimeListAsync();
        result.Should().NotBeNull();
        result.Should().HaveCount(5);
    }

    #endregion

    #region Anime Images Tests

    [Fact]
    public async Task GetAnimeImagesCachedAsync_WhenInMemory_ReturnsFromMemory()
    {
        // Arrange
        var bangumiId = 12345;
        var cacheKey = $"anime_images_{bangumiId}";
        var images = new AnimeImagesEntity
        {
            BangumiId = bangumiId,
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg"
        };
        _memoryCache.Set(cacheKey, images);

        // Act
        var result = await _sut.GetAnimeImagesCachedAsync(bangumiId);

        // Assert
        result.Should().NotBeNull();
        result!.BangumiId.Should().Be(bangumiId);
        _repositoryMock.Verify(r => r.GetAnimeImagesAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetAnimeImagesCachedAsync_WhenNotInMemory_FetchesFromSqlite()
    {
        // Arrange
        var bangumiId = 12345;
        var images = new AnimeImagesEntity
        {
            BangumiId = bangumiId,
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg"
        };
        _repositoryMock
            .Setup(r => r.GetAnimeImagesAsync(bangumiId))
            .ReturnsAsync(images);

        // Act
        var result = await _sut.GetAnimeImagesCachedAsync(bangumiId);

        // Assert
        result.Should().NotBeNull();
        result!.BangumiId.Should().Be(bangumiId);
        _repositoryMock.Verify(r => r.GetAnimeImagesAsync(bangumiId), Times.Once);
    }

    [Fact]
    public async Task CacheAnimeImagesAsync_SavesToBothCaches()
    {
        // Arrange
        var bangumiId = 12345;
        var posterUrl = "https://example.com/poster.jpg";
        var backdropUrl = "https://example.com/backdrop.jpg";

        // Act
        await _sut.CacheAnimeImagesAsync(bangumiId, posterUrl, backdropUrl, null);

        // Assert
        _repositoryMock.Verify(r => r.SaveAnimeImagesAsync(It.Is<AnimeImagesEntity>(
            e => e.BangumiId == bangumiId &&
                 e.PosterUrl == posterUrl &&
                 e.BackdropUrl == backdropUrl)), Times.Once);

        // Verify memory cache was populated
        var result = await _sut.GetAnimeImagesCachedAsync(bangumiId);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAnimeImagesBatchCachedAsync_ReturnsMixedResults()
    {
        // Arrange
        var ids = new List<int> { 1, 2, 3 };

        // Put ID 1 in memory cache
        var cached1 = new AnimeImagesEntity { BangumiId = 1, PosterUrl = "url1" };
        _memoryCache.Set("anime_images_1", cached1);

        // Set up repository to return IDs 2 and 3
        var dbImages = new List<AnimeImagesEntity>
        {
            new() { BangumiId = 2, PosterUrl = "url2" },
            new() { BangumiId = 3, PosterUrl = "url3" }
        };
        _repositoryMock
            .Setup(r => r.GetAnimeImagesBatchAsync(It.Is<List<int>>(l => l.Contains(2) && l.Contains(3))))
            .ReturnsAsync(dbImages);

        // Act
        var result = await _sut.GetAnimeImagesBatchCachedAsync(ids);

        // Assert
        result.Should().HaveCount(3);
        result[1].PosterUrl.Should().Be("url1");
        result[2].PosterUrl.Should().Be("url2");
        result[3].PosterUrl.Should().Be("url3");
    }

    #endregion
}
