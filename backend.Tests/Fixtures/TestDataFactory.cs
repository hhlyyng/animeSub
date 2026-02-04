using System.Text.Json;
using backend.Models;
using backend.Models.Dtos;
using backend.Models.Jikan;

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

    /// <summary>
    /// Create TMDB search response with mixed anime/live-action results
    /// </summary>
    public static string CreateTMDBMixedSearchResponse()
    {
        return JsonSerializer.Serialize(new
        {
            results = new object[]
            {
                new { id = 1, name = "Live Action Show", genre_ids = new[] { 18 }, origin_country = new[] { "US" } },
                new { id = 2, name = "Anime Show", genre_ids = new[] { 16 }, origin_country = new[] { "JP" } },
                new { id = 3, name = "Another Show", genre_ids = new[] { 35 }, origin_country = new[] { "KR" } }
            }
        });
    }

    /// <summary>
    /// Create TMDB TV details with multiple seasons
    /// </summary>
    public static string CreateTMDBTvDetailsWithSeasons()
    {
        return JsonSerializer.Serialize(new
        {
            id = 12345,
            name = "Multi-Season Anime",
            seasons = new object[]
            {
                new { season_number = 1, air_date = "2020-01-10", poster_path = "/s1.jpg" },
                new { season_number = 2, air_date = "2022-04-01", poster_path = "/s2.jpg" },
                new { season_number = 3, air_date = "2024-01-06", poster_path = "/s3.jpg" }
            }
        });
    }

    /// <summary>
    /// Create a sample Jikan API top anime response
    /// </summary>
    public static string CreateJikanTopAnimeResponse(int count = 10)
    {
        var animes = Enumerable.Range(1, count).Select(i => new
        {
            mal_id = i,
            url = $"https://myanimelist.net/anime/{i}",
            title = $"Test Anime {i}",
            title_japanese = $"テストアニメ {i}",
            title_english = $"Test Anime EN {i}",
            synopsis = $"This is a test synopsis for anime {i}",
            score = 9.1 - (i * 0.05),
            scored_by = 100000 - (i * 1000),
            rank = i,
            popularity = i * 10,
            status = "Finished Airing",
            images = new
            {
                jpg = new
                {
                    image_url = $"https://cdn.myanimelist.net/images/anime/{i}.jpg",
                    small_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_small.jpg",
                    large_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_large.jpg"
                }
            }
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            data = animes,
            pagination = new { last_visible_page = 1, has_next_page = false }
        });
    }

    /// <summary>
    /// Create a sample JikanAnimeInfo for testing
    /// </summary>
    public static JikanAnimeInfo CreateJikanAnimeInfo(int malId = 1, string title = "Test Anime")
    {
        return new JikanAnimeInfo
        {
            MalId = malId,
            Title = title,
            TitleJapanese = $"テスト{title}",
            TitleEnglish = $"{title} EN",
            Synopsis = $"Synopsis for {title}",
            Score = 8.5,
            ScoredBy = 50000,
            Rank = malId,
            Popularity = malId * 10,
            Status = "Finished Airing",
            Url = $"https://myanimelist.net/anime/{malId}",
            Images = new JikanImages
            {
                Jpg = new JikanImageFormat
                {
                    ImageUrl = $"https://cdn.myanimelist.net/images/anime/{malId}.jpg",
                    LargeImageUrl = $"https://cdn.myanimelist.net/images/anime/{malId}_large.jpg"
                }
            }
        };
    }

    /// <summary>
    /// Create a sample AniList GraphQL trending response
    /// </summary>
    public static string CreateAniListTrendingResponse(int count = 10)
    {
        var media = Enumerable.Range(1, count).Select(i => new
        {
            id = i,
            title = new
            {
                romaji = $"Test Anime {i}",
                native = $"テストアニメ {i}",
                english = $"Test Anime EN {i}"
            },
            description = $"Description for anime {i}",
            averageScore = 85 - i,
            coverImage = new
            {
                large = $"https://anilist.co/img/cover_{i}.jpg",
                extraLarge = $"https://anilist.co/img/cover_{i}_xl.jpg"
            },
            bannerImage = $"https://anilist.co/img/banner_{i}.jpg",
            siteUrl = $"https://anilist.co/anime/{i}"
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            data = new
            {
                Page = new { media = media }
            }
        });
    }

    /// <summary>
    /// Create a sample Bangumi top subjects search response
    /// </summary>
    public static string CreateBangumiTopSubjectsResponse(int count = 10)
    {
        var subjects = Enumerable.Range(1, count).Select(i => new
        {
            id = i,
            name = $"テストアニメ {i}",
            name_cn = $"测试动漫 {i}",
            summary = $"这是测试动漫 {i} 的简介",
            score = 9.5 - (i * 0.1),
            rank = i,
            images = new
            {
                large = $"https://bgm.tv/img/cover_{i}.jpg",
                medium = $"https://bgm.tv/img/cover_{i}_m.jpg"
            }
        }).ToArray();

        return JsonSerializer.Serialize(new { data = subjects });
    }
}
