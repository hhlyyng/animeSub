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
    private readonly Mock<IMikanClient> _mikanClientMock;
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
        _mikanClientMock = new Mock<IMikanClient>();
        _repositoryMock = new Mock<IAnimeRepository>();
        _cacheServiceMock = new Mock<IAnimeCacheService>();
        _resilienceServiceMock = new Mock<IResilienceService>();
        _loggerMock = new Mock<ILogger<AnimeAggregationService>>();

        _repositoryMock
            .Setup(r => r.GetTopAnimeCacheAsync(It.IsAny<string>()))
            .ReturnsAsync((TopAnimeCacheEntity?)null);
        _repositoryMock
            .Setup(r => r.SaveTopAnimeCacheAsync(It.IsAny<TopAnimeCacheEntity>()))
            .Returns(Task.CompletedTask);

        _sut = new AnimeAggregationService(
            _bangumiClientMock.Object,
            _tmdbClientMock.Object,
            _aniListClientMock.Object,
            _jikanClientMock.Object,
            _mikanClientMock.Object,
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
        var result = await _sut.GetTodayAnimeEnrichedAsync("tmdb-token");

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
        var result = await _sut.GetTodayAnimeEnrichedAsync(null);

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
        await _sut.GetTodayAnimeEnrichedAsync("tmdb-token");

        // Assert
        _tmdbClientMock.Verify(t => t.SetToken("tmdb-token"), Times.Once);
    }

    [Fact]
    public async Task GetTodayAnimeEnrichedAsync_WhenEntityHasMikanBangumiId_MapsToDto()
    {
        // Arrange
        var entity = CreateAnimeInfoEntities(1).First();
        entity.MikanBangumiId = "229";

        _repositoryMock
            .Setup(r => r.GetAnimesByWeekdayAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AnimeInfoEntity> { entity });

        _resilienceServiceMock
            .Setup(r => r.ExecuteWithRetryAndMetadataAsync(
                It.IsAny<Func<CancellationToken, Task<JsonElement>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateEmptyJsonArray(), 0, true));

        // Act
        var result = await _sut.GetTodayAnimeEnrichedAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Animes.Should().HaveCount(1);
        result.Animes[0].MikanBangumiId.Should().Be("229");
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

    #region Top10 DB-First Tests

    [Fact]
    public async Task GetTopAnimeFromBangumiAsync_WhenCacheComplete_DoesNotCallExternalEnrichment()
    {
        // Arrange
        var topSubjects = JsonDocument.Parse(
            """
            [
              {
                "id": 100,
                "name": "Frieren",
                "name_cn": "葬送的芙莉莲",
                "summary": "summary",
                "date": "2025-01-01",
                "rating": { "score": 8.8 },
                "images": { "large": "https://example.com/portrait.jpg" }
              }
            ]
            """).RootElement;

        _bangumiClientMock
            .Setup(b => b.SearchTopSubjectsAsync(10))
            .ReturnsAsync(topSubjects);

        _repositoryMock
            .Setup(r => r.GetAnimeInfoBatchAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<AnimeInfoEntity>
            {
                new()
                {
                    BangumiId = 100,
                    NameJapanese = "Frieren",
                    NameChinese = "葬送的芙莉莲",
                    NameEnglish = "Frieren: Beyond Journey's End",
                    DescChinese = "cached zh",
                    DescEnglish = "cached en",
                    ImagePortrait = "https://example.com/portrait-db.jpg",
                    ImageLandscape = "https://example.com/landscape-db.jpg",
                    UrlTmdb = "https://www.themoviedb.org/tv/123",
                    UrlAnilist = "https://anilist.co/anime/123"
                }
            });

        _repositoryMock
            .Setup(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetTopAnimeFromBangumiAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Animes[0].Images.Landscape.Should().Be("https://example.com/landscape-db.jpg");

        _tmdbClientMock.Verify(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _aniListClientMock.Verify(a => a.GetAnimeInfoAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()), Times.Once);
    }

    [Fact]
    public async Task GetTopAnimeFromBangumiAsync_WhenCacheMissing_BackfillsAndPersists()
    {
        // Arrange
        var topSubjects = JsonDocument.Parse(
            """
            [
              {
                "id": 101,
                "name": "Dandadan",
                "name_cn": "胆大党",
                "summary": "summary",
                "date": "2025-02-01",
                "rating": { "score": 8.2 },
                "images": { "large": "https://example.com/portrait2.jpg" }
              }
            ]
            """).RootElement;

        _bangumiClientMock
            .Setup(b => b.SearchTopSubjectsAsync(10))
            .ReturnsAsync(topSubjects);

        _repositoryMock
            .Setup(r => r.GetAnimeInfoBatchAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<AnimeInfoEntity>());

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync("Dandadan", "2025-02-01"))
            .ReturnsAsync(TestDataFactory.CreateTMDBAnimeInfo());

        _repositoryMock
            .Setup(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetTopAnimeFromBangumiAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        _tmdbClientMock.Verify(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()), Times.Once);
    }

    [Fact]
    public async Task GetTopAnimeFromMALAsync_WhenTitleCacheHit_DoesNotCallBangumiOrTmdb()
    {
        // Arrange
        _jikanClientMock
            .Setup(j => j.GetTopAnimeAsync(10))
            .ReturnsAsync(new List<backend.Models.Jikan.JikanAnimeInfo>
            {
                new()
                {
                    MalId = 1,
                    Title = "Sousou no Frieren",
                    TitleJapanese = "葬送のフリーレン",
                    TitleEnglish = "Frieren: Beyond Journey's End",
                    Synopsis = "synopsis",
                    Score = 9.1,
                    Url = "https://myanimelist.net/anime/1",
                    Images = new backend.Models.Jikan.JikanImages
                    {
                        Jpg = new backend.Models.Jikan.JikanImageFormat
                        {
                            LargeImageUrl = "https://example.com/mal-portrait.jpg"
                        }
                    }
                }
            });

        _repositoryMock
            .Setup(r => r.FindAnimeInfoByAnyTitleAsync(It.IsAny<string?[]>()))
            .ReturnsAsync(new AnimeInfoEntity
            {
                BangumiId = 515759,
                NameJapanese = "葬送のフリーレン",
                NameChinese = "葬送的芙莉莲",
                DescChinese = "cached zh",
                ImageLandscape = "https://example.com/cached-landscape.jpg",
                UrlBangumi = "https://bgm.tv/subject/515759",
                UrlTmdb = "https://www.themoviedb.org/tv/123",
                UrlAnilist = "https://anilist.co/anime/123"
            });

        _repositoryMock
            .Setup(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetTopAnimeFromMALAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Animes[0].ChTitle.Should().Be("葬送的芙莉莲");

        _bangumiClientMock.Verify(b => b.SearchByTitleAsync(It.IsAny<string>()), Times.Never);
        _tmdbClientMock.Verify(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()), Times.Once);
    }

    [Fact]
    public async Task GetTopAnimeFromAniListAsync_WhenPersistentCacheExists_ReturnsDatabaseSnapshot()
    {
        // Arrange
        var cached = new List<AnimeInfoDto>
        {
            new()
            {
                BangumiId = "515759",
                JpTitle = "葬送のフリーレン 第2期",
                ChTitle = "葬送的芙莉莲 第二季",
                EnTitle = "Frieren: Beyond Journey's End Season 2",
                Images = new AnimeImagesDto
                {
                    Portrait = "https://example.com/p.jpg",
                    Landscape = "https://example.com/l.jpg"
                },
                Score = "9.2"
            }
        };

        _repositoryMock
            .Setup(r => r.GetTopAnimeCacheAsync("top:anilist"))
            .ReturnsAsync(new TopAnimeCacheEntity
            {
                Source = "top:anilist",
                PayloadJson = JsonSerializer.Serialize(cached),
                UpdatedAt = DateTime.UtcNow.AddHours(-1)
            });

        // Act
        var result = await _sut.GetTopAnimeFromAniListAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.DataSource.Should().Be(DataSource.Database);
        result.Count.Should().Be(1);
        result.Animes[0].BangumiId.Should().Be("515759");

        _aniListClientMock.Verify(a => a.GetTrendingAnimeAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetTopAnimeFromAniListAsync_WhenApiSucceeds_PersistsTopCacheSnapshot()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetTopAnimeCacheAsync("top:anilist"))
            .ReturnsAsync((TopAnimeCacheEntity?)null);

        _aniListClientMock
            .Setup(a => a.GetTrendingAnimeAsync(10))
            .ReturnsAsync(new List<AniListAnimeInfo>
            {
                new()
                {
                    NativeTitle = "葬送のフリーレン",
                    EnglishTitle = "Frieren: Beyond Journey's End",
                    EnglishSummary = "summary",
                    Score = "9.1",
                    CoverUrl = "https://example.com/cover.jpg",
                    BannerImage = "https://example.com/banner.jpg",
                    OriSiteUrl = "https://anilist.co/anime/154587"
                }
            });

        _bangumiClientMock
            .Setup(b => b.SearchByTitleAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse("{\"id\":459283,\"name_cn\":\"葬送的芙莉莲\",\"summary\":\"summary\"}").RootElement);

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((TMDBAnimeInfo?)null);

        // Act
        var result = await _sut.GetTopAnimeFromAniListAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.SaveTopAnimeCacheAsync(It.Is<TopAnimeCacheEntity>(c => c.Source == "top:anilist")),
            Times.Once);
    }

    [Fact]
    public async Task GetTopAnimeFromMALAsync_WhenBangumiUnresolved_UsesMalFallbackId()
    {
        // Arrange
        _jikanClientMock
            .Setup(j => j.GetTopAnimeAsync(10))
            .ReturnsAsync(new List<backend.Models.Jikan.JikanAnimeInfo>
            {
                new()
                {
                    MalId = 11061,
                    Title = "Hunter x Hunter",
                    TitleJapanese = "HUNTER×HUNTER（ハンター×ハンター）",
                    TitleEnglish = "Hunter x Hunter",
                    Url = "https://myanimelist.net/anime/11061/Hunter_x_Hunter_2011",
                    Score = 9.0,
                    Synopsis = "synopsis",
                    Images = new backend.Models.Jikan.JikanImages
                    {
                        Jpg = new backend.Models.Jikan.JikanImageFormat
                        {
                            LargeImageUrl = "https://example.com/hxh.jpg"
                        }
                    }
                }
            });

        _repositoryMock
            .Setup(r => r.FindAnimeInfoByAnyTitleAsync(It.IsAny<string?[]>()))
            .ReturnsAsync((AnimeInfoEntity?)null);

        _bangumiClientMock
            .Setup(b => b.SearchByTitleAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((TMDBAnimeInfo?)null);

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync((AniListAnimeInfo?)null);

        // Act
        var result = await _sut.GetTopAnimeFromMALAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Animes[0].BangumiId.Should().Be("mal:11061");
    }

    [Fact]
    public async Task GetTopAnimeFromMALAsync_WhenCacheTitleMismatched_IgnoresCacheAndUsesBangumiLookup()
    {
        // Arrange
        _jikanClientMock
            .Setup(j => j.GetTopAnimeAsync(10))
            .ReturnsAsync(new List<backend.Models.Jikan.JikanAnimeInfo>
            {
                new()
                {
                    MalId = 59978,
                    Title = "Sousou no Frieren 2nd Season",
                    TitleJapanese = "葬送のフリーレン 第2期",
                    TitleEnglish = "Frieren: Beyond Journey's End Season 2",
                    Url = "https://myanimelist.net/anime/59978/Sousou_no_Frieren_2nd_Season",
                    Score = 9.2,
                    Synopsis = "synopsis"
                }
            });

        _repositoryMock
            .Setup(r => r.FindAnimeInfoByAnyTitleAsync(It.IsAny<string?[]>()))
            .ReturnsAsync(new AnimeInfoEntity
            {
                BangumiId = 459283,
                NameJapanese = "葬送のフリーレン",
                NameChinese = "葬送的芙莉莲 ～●●的魔法～",
                NameEnglish = "Frieren: Beyond Journey's End"
            });

        _bangumiClientMock
            .Setup(b => b.SearchByTitleAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse(
                """
                {
                  "id": 515759,
                  "name_cn": "葬送的芙莉莲 第二季",
                  "summary": "season2"
                }
                """).RootElement);

        _repositoryMock
            .Setup(r => r.GetAnimeInfoAsync(515759))
            .ReturnsAsync((AnimeInfoEntity?)null);

        _tmdbClientMock
            .Setup(t => t.GetAnimeSummaryAndBackdropAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((TMDBAnimeInfo?)null);

        _aniListClientMock
            .Setup(a => a.GetAnimeInfoAsync(It.IsAny<string>()))
            .ReturnsAsync((AniListAnimeInfo?)null);

        _repositoryMock
            .Setup(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetTopAnimeFromMALAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Animes[0].BangumiId.Should().Be("515759");
        result.Animes[0].ChTitle.Should().Be("葬送的芙莉莲 第二季");

        _bangumiClientMock.Verify(b => b.SearchByTitleAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetTopAnimeFromMALAsync_WhenTitleMatchesKnownOverridePattern_NormalizesChineseTitle()
    {
        // Arrange
        _jikanClientMock
            .Setup(j => j.GetTopAnimeAsync(10))
            .ReturnsAsync(new List<backend.Models.Jikan.JikanAnimeInfo>
            {
                new()
                {
                    MalId = 52991,
                    Title = "Sousou no Frieren",
                    TitleJapanese = "葬送のフリーレン",
                    TitleEnglish = "Frieren: Beyond Journey's End",
                    Url = "https://myanimelist.net/anime/52991/Sousou_no_Frieren",
                    Score = 9.1,
                    Synopsis = "synopsis",
                    Images = new backend.Models.Jikan.JikanImages
                    {
                        Jpg = new backend.Models.Jikan.JikanImageFormat
                        {
                            LargeImageUrl = "https://example.com/mal-portrait.jpg"
                        }
                    }
                }
            });

        _repositoryMock
            .Setup(r => r.FindAnimeInfoByAnyTitleAsync(It.IsAny<string?[]>()))
            .ReturnsAsync(new AnimeInfoEntity
            {
                BangumiId = 459283,
                NameJapanese = "葬送のフリーレン",
                NameChinese = "葬送的芙莉莲 ～●●的魔法～",
                NameEnglish = "Frieren: Beyond Journey's End",
                ImageLandscape = "https://example.com/cached-landscape.jpg",
                UrlBangumi = "https://bgm.tv/subject/459283",
                UrlTmdb = "https://www.themoviedb.org/tv/123",
                UrlAnilist = "https://anilist.co/anime/123"
            });

        _repositoryMock
            .Setup(r => r.SaveAnimeInfoAsync(It.IsAny<AnimeInfoEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetTopAnimeFromMALAsync(limit: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Animes[0].ChTitle.Should().Be("葬送的芙莉莲");
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
