using System.Text.Json;
using backend.Models;
using backend.Models.Dtos;

namespace backend.Tests.Fixtures;

/// <summary>
/// Factory for creating test data
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Create a sample AnimeInfoDto for testing
    /// </summary>
    public static AnimeInfoDto CreateAnimeInfoDto(
        string bangumiId = "12345",
        string jpTitle = "テストアニメ",
        string chTitle = "测试动画",
        string enTitle = "Test Anime")
    {
        return new AnimeInfoDto
        {
            BangumiId = bangumiId,
            JpTitle = jpTitle,
            ChTitle = chTitle,
            EnTitle = enTitle,
            ChDesc = "这是一部测试动画",
            EnDesc = "This is a test anime",
            Score = "8.5",
            Images = new AnimeImagesDto
            {
                Portrait = "https://example.com/poster.jpg",
                Landscape = "https://example.com/backdrop.jpg"
            },
            ExternalUrls = new ExternalUrlsDto
            {
                Bangumi = $"https://bgm.tv/subject/{bangumiId}",
                Tmdb = "https://www.themoviedb.org/tv/12345",
                Anilist = "https://anilist.co/anime/12345"
            }
        };
    }

    /// <summary>
    /// Create a list of sample AnimeInfoDto for testing
    /// </summary>
    public static List<AnimeInfoDto> CreateAnimeInfoDtoList(int count = 5)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateAnimeInfoDto(
                bangumiId: i.ToString(),
                jpTitle: $"テストアニメ{i}",
                chTitle: $"测试动画{i}",
                enTitle: $"Test Anime {i}"))
            .ToList();
    }

    /// <summary>
    /// Create a sample AnimeListResponse for testing
    /// </summary>
    public static AnimeListResponse CreateAnimeListResponse(
        bool success = true,
        DataSource dataSource = DataSource.Api,
        bool isStale = false,
        int count = 5)
    {
        var animes = CreateAnimeInfoDtoList(count);
        return new AnimeListResponse
        {
            Success = success,
            DataSource = dataSource,
            IsStale = isStale,
            Message = dataSource switch
            {
                DataSource.Api => "Data refreshed from API",
                DataSource.Cache => "Data from cache (up to date)",
                DataSource.CacheFallback => "API failed. Showing cached data.",
                _ => "Unknown"
            },
            LastUpdated = DateTime.UtcNow,
            Count = count,
            Animes = animes,
            RetryAttempts = 0
        };
    }

    /// <summary>
    /// Create a sample Bangumi API JSON response
    /// </summary>
    public static JsonElement CreateBangumiApiResponse(int count = 3)
    {
        var animes = Enumerable.Range(1, count).Select(i => new
        {
            id = i,
            name = $"テストアニメ{i}",
            name_cn = $"测试动画{i}",
            summary = $"这是测试动画{i}的简介",
            rating = new { score = 8.0 + (i * 0.1), total = 1000 },
            images = new { large = $"https://example.com/poster{i}.jpg" }
        }).ToArray();

        var json = JsonSerializer.Serialize(animes);
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Create a sample TMDBAnimeInfo for testing
    /// </summary>
    public static TMDBAnimeInfo CreateTMDBAnimeInfo(string title = "Test Anime")
    {
        return new TMDBAnimeInfo
        {
            TMDBID = "12345",
            EnglishTitle = title,
            ChineseTitle = "测试动画",
            EnglishSummary = "This is a test anime from TMDB",
            ChineseSummary = "这是来自TMDB的测试动画",
            BackdropUrl = "https://image.tmdb.org/t/p/original/backdrop.jpg",
            OriSiteUrl = "https://www.themoviedb.org/tv/12345"
        };
    }

    /// <summary>
    /// Create a sample AniListAnimeInfo for testing
    /// </summary>
    public static AniListAnimeInfo CreateAniListAnimeInfo(string title = "Test Anime")
    {
        return new AniListAnimeInfo
        {
            AnilistId = "12345",
            EnglishTitle = title,
            EnglishSummary = "This is a test anime from AniList",
            CoverUrl = "https://anilist.co/img/cover.jpg",
            OriSiteUrl = "https://anilist.co/anime/12345"
        };
    }
}
