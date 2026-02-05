using System.Text.Json;
using backend.Data.Entities;
using backend.Models;
using backend.Models.Dtos;
using backend.Models.Jikan;
using backend.Services.Interfaces;
using backend.Services.Repositories;

namespace backend.Services.Implementations;

/// <summary>
/// Service that aggregates anime data from multiple sources
/// Priority: Pre-fetched DB data > Real-time API fetch
/// </summary>
public class AnimeAggregationService : IAnimeAggregationService
{
    private readonly IBangumiClient _bangumiClient;
    private readonly ITMDBClient _tmdbClient;
    private readonly IAniListClient _aniListClient;
    private readonly IJikanClient _jikanClient;
    private readonly IAnimeRepository _repository;
    private readonly IAnimeCacheService _cacheService;
    private readonly IResilienceService _resilienceService;
    private readonly ILogger<AnimeAggregationService> _logger;

    public AnimeAggregationService(
        IBangumiClient bangumiClient,
        ITMDBClient tmdbClient,
        IAniListClient aniListClient,
        IJikanClient jikanClient,
        IAnimeRepository repository,
        IAnimeCacheService cacheService,
        IResilienceService resilienceService,
        ILogger<AnimeAggregationService> logger)
    {
        _bangumiClient = bangumiClient ?? throw new ArgumentNullException(nameof(bangumiClient));
        _tmdbClient = tmdbClient ?? throw new ArgumentNullException(nameof(tmdbClient));
        _aniListClient = aniListClient ?? throw new ArgumentNullException(nameof(aniListClient));
        _jikanClient = jikanClient ?? throw new ArgumentNullException(nameof(jikanClient));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AnimeListResponse> GetTodayAnimeEnrichedAsync(
        string? bangumiToken = null,
        string? tmdbToken = null,
        CancellationToken cancellationToken = default)
    {
        // Bangumi public API doesn't require authentication
        // Set tokens on clients (if provided)
        if (!string.IsNullOrWhiteSpace(bangumiToken))
            _bangumiClient.SetToken(bangumiToken);
        _tmdbClient.SetToken(tmdbToken);

        _logger.LogInformation("Starting anime aggregation for today's schedule");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Get today's weekday (1-7, Monday-Sunday)
        int todayWeekday = (int)DateTime.Now.DayOfWeek;
        if (todayWeekday == 0) todayWeekday = 7; // Sunday = 7

        try
        {
            // Step 1: Try to get pre-fetched data from database
            var preFetchedAnimes = await _repository.GetAnimesByWeekdayAsync(todayWeekday);

            if (preFetchedAnimes.Count > 0)
            {
                _logger.LogInformation("Found {Count} pre-fetched anime for weekday {Weekday}",
                    preFetchedAnimes.Count, todayWeekday);

                // Step 2: Fetch current Bangumi schedule to check for new anime
                var (currentBangumiIds, apiSuccess) = await GetCurrentBangumiIdsAsync(cancellationToken);

                if (apiSuccess && currentBangumiIds.Count > 0)
                {
                    // Find new anime not in pre-fetched data
                    var preFetchedIds = preFetchedAnimes.Select(a => a.BangumiId).ToHashSet();
                    var newAnimeIds = currentBangumiIds.Where(id => !preFetchedIds.Contains(id)).ToList();

                    if (newAnimeIds.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} new anime not in pre-fetch, fetching in real-time", newAnimeIds.Count);

                        // Fetch new anime in real-time
                        var newAnimes = await FetchNewAnimesAsync(newAnimeIds, todayWeekday, cancellationToken);
                        preFetchedAnimes.AddRange(newAnimes);
                    }
                }

                // Convert to DTOs
                var animeDtos = preFetchedAnimes.Select(ConvertEntityToDto).ToList();

                stopwatch.Stop();
                _logger.LogInformation("Returned {Count} anime from pre-fetch + real-time in {Elapsed}ms",
                    animeDtos.Count, stopwatch.ElapsedMilliseconds);

                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.Database,
                    IsStale = false,
                    Message = $"Data from pre-fetch database ({preFetchedAnimes.Count} cached)",
                    LastUpdated = DateTime.UtcNow,
                    Count = animeDtos.Count,
                    Animes = animeDtos,
                    RetryAttempts = 0
                };
            }

            // Step 3: No pre-fetched data, fall back to real-time API fetch
            _logger.LogWarning("No pre-fetched data for weekday {Weekday}, falling back to real-time fetch", todayWeekday);
            return await FetchAllFromApiAsync(bangumiToken, tmdbToken, todayWeekday, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anime aggregation");

            // Try memory cache fallback
            var cachedList = await _cacheService.GetCachedAnimeListAsync();
            if (cachedList != null && cachedList.Count > 0)
            {
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"Error occurred, showing cached data: {ex.Message}",
                    LastUpdated = null,
                    Count = cachedList.Count,
                    Animes = cachedList,
                    RetryAttempts = 0
                };
            }

            throw;
        }
    }

    private async Task<(List<int> ids, bool success)> GetCurrentBangumiIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (bangumiData, _, apiSuccess) = await _resilienceService.ExecuteWithRetryAndMetadataAsync(
                async ct => await _bangumiClient.GetDailyBroadcastAsync(),
                "Bangumi.GetDailyBroadcast",
                cancellationToken);

            if (!apiSuccess || bangumiData.ValueKind == JsonValueKind.Undefined)
                return (new List<int>(), false);

            var ids = bangumiData.EnumerateArray()
                .Select(a => a.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0)
                .Where(id => id > 0)
                .ToList();

            return (ids, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch current Bangumi IDs");
            return (new List<int>(), false);
        }
    }

    private async Task<List<AnimeInfoEntity>> FetchNewAnimesAsync(
        List<int> bangumiIds,
        int weekday,
        CancellationToken cancellationToken)
    {
        var results = new List<AnimeInfoEntity>();

        foreach (var bangumiId in bangumiIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var anime = await FetchSingleAnimeAsync(bangumiId, weekday, cancellationToken);
                if (anime != null)
                {
                    results.Add(anime);
                    // Save to database for future use
                    await _repository.SaveAnimeInfoAsync(anime);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch new anime BangumiId {BangumiId}", bangumiId);
            }
        }

        return results;
    }

    private async Task<AnimeInfoEntity?> FetchSingleAnimeAsync(
        int bangumiId,
        int weekday,
        CancellationToken cancellationToken)
    {
        // Fetch subject detail from Bangumi
        var subjectDetail = await _bangumiClient.GetSubjectDetailAsync(bangumiId);

        var oriTitle = subjectDetail.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var chTitle = subjectDetail.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";
        var chDesc = subjectDetail.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";
        var airDate = subjectDetail.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;

        var score = subjectDetail.TryGetProperty("rating", out var rating) &&
                    rating.TryGetProperty("score", out var scoreEl)
                    ? scoreEl.GetDouble().ToString("F1")
                    : "0";

        var portraitUrl = subjectDetail.TryGetProperty("images", out var images) &&
                         images.TryGetProperty("large", out var large)
                         ? large.GetString() ?? ""
                         : "";

        bool containsJapanese = !string.IsNullOrEmpty(oriTitle) &&
            System.Text.RegularExpressions.Regex.IsMatch(oriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

        // Fetch TMDB and AniList in parallel
        var tmdbTask = _tmdbClient.GetAnimeSummaryAndBackdropAsync(oriTitle, airDate);
        var anilistTask = _aniListClient.GetAnimeInfoAsync(oriTitle);

        try
        {
            await Task.WhenAll(tmdbTask, anilistTask);
        }
        catch { /* Continue with partial results */ }

        var tmdbResult = tmdbTask.IsCompletedSuccessfully ? await tmdbTask : null;
        var anilistResult = anilistTask.IsCompletedSuccessfully ? await anilistTask : null;

        // Determine final values
        var jpTitle = containsJapanese ? oriTitle : "";
        var enTitle = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "";

        // Skip if no valid title
        if (string.IsNullOrEmpty(jpTitle) && string.IsNullOrEmpty(chTitle) && string.IsNullOrEmpty(enTitle))
        {
            _logger.LogWarning("Skipping anime BangumiId {BangumiId} - no valid title", bangumiId);
            return null;
        }

        // Check if description is Japanese
        bool descIsJapanese = !string.IsNullOrEmpty(chDesc) &&
            System.Text.RegularExpressions.Regex.IsMatch(chDesc, @"[\p{IsHiragana}\p{IsKatakana}]");

        string finalChDesc;
        if (!string.IsNullOrEmpty(chDesc) && !descIsJapanese)
            finalChDesc = chDesc;
        else if (!string.IsNullOrEmpty(tmdbResult?.ChineseSummary))
            finalChDesc = tmdbResult.ChineseSummary;
        else
            finalChDesc = "无可用中文介绍";

        string finalEnDesc;
        if (!string.IsNullOrEmpty(tmdbResult?.EnglishSummary) &&
            tmdbResult.EnglishSummary != "No English summary available." &&
            tmdbResult.EnglishSummary != "No result found in TMDB.")
            finalEnDesc = tmdbResult.EnglishSummary;
        else if (!string.IsNullOrEmpty(anilistResult?.EnglishSummary))
            finalEnDesc = anilistResult.EnglishSummary;
        else
            finalEnDesc = "No English description available";

        return new AnimeInfoEntity
        {
            BangumiId = bangumiId,
            NameJapanese = jpTitle,
            NameChinese = chTitle,
            NameEnglish = enTitle,
            DescChinese = finalChDesc,
            DescEnglish = finalEnDesc,
            Score = score,
            ImagePortrait = portraitUrl,
            ImageLandscape = tmdbResult?.BackdropUrl ?? "",
            TmdbId = int.TryParse(tmdbResult?.TMDBID, out var tmdbId) ? tmdbId : null,
            AnilistId = int.TryParse(anilistResult?.AnilistId, out var anilistId) ? anilistId : null,
            UrlBangumi = $"https://bgm.tv/subject/{bangumiId}",
            UrlTmdb = tmdbResult?.OriSiteUrl ?? "",
            UrlAnilist = anilistResult?.OriSiteUrl ?? "",
            AirDate = airDate,
            Weekday = weekday,
            IsPreFetched = false // Real-time fetched
        };
    }

    private async Task<AnimeListResponse> FetchAllFromApiAsync(
        string? bangumiToken,
        string? tmdbToken,
        int weekday,
        CancellationToken cancellationToken)
    {
        var (bangumiData, retryCount, apiSuccess) = await _resilienceService.ExecuteWithRetryAndMetadataAsync(
            async ct => await _bangumiClient.GetDailyBroadcastAsync(),
            "Bangumi.GetDailyBroadcast",
            cancellationToken);

        if (!apiSuccess || bangumiData.ValueKind == JsonValueKind.Undefined)
        {
            return new AnimeListResponse
            {
                Success = false,
                DataSource = DataSource.Api,
                IsStale = true,
                Message = $"API request failed after {retryCount} retries",
                LastUpdated = null,
                Count = 0,
                Animes = new List<AnimeInfoDto>(),
                RetryAttempts = retryCount
            };
        }

        var enrichedAnimes = new List<AnimeInfoEntity>();

        foreach (var anime in bangumiData.EnumerateArray())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var bangumiId = anime.GetProperty("id").GetInt32();
                var enriched = await FetchSingleAnimeAsync(bangumiId, weekday, cancellationToken);
                if (enriched != null)
                {
                    enrichedAnimes.Add(enriched);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing individual anime");
            }
        }

        // Save to database
        if (enrichedAnimes.Count > 0)
        {
            await _repository.SaveAnimeInfoBatchAsync(enrichedAnimes);
        }

        var animeDtos = enrichedAnimes.Select(ConvertEntityToDto).ToList();

        // Also cache in memory for quick access
        await _cacheService.CacheAnimeListAsync(animeDtos);

        return new AnimeListResponse
        {
            Success = true,
            DataSource = DataSource.Api,
            IsStale = false,
            Message = retryCount > 0
                ? $"Data refreshed from API (succeeded after {retryCount} retries)"
                : "Data refreshed from API",
            LastUpdated = DateTime.UtcNow,
            Count = animeDtos.Count,
            Animes = animeDtos,
            RetryAttempts = retryCount
        };
    }

    private static AnimeInfoDto ConvertEntityToDto(AnimeInfoEntity entity)
    {
        return new AnimeInfoDto
        {
            BangumiId = entity.BangumiId.ToString(),
            JpTitle = entity.NameJapanese ?? "",
            ChTitle = entity.NameChinese ?? "",
            EnTitle = entity.NameEnglish ?? "",
            ChDesc = entity.DescChinese ?? "无可用中文介绍",
            EnDesc = entity.DescEnglish ?? "No English description available",
            Score = entity.Score ?? "0",
            Images = new AnimeImagesDto
            {
                Portrait = entity.ImagePortrait ?? "",
                Landscape = entity.ImageLandscape ?? ""
            },
            ExternalUrls = new ExternalUrlsDto
            {
                Bangumi = entity.UrlBangumi ?? $"https://bgm.tv/subject/{entity.BangumiId}",
                Tmdb = entity.UrlTmdb ?? "",
                Anilist = entity.UrlAnilist ?? ""
            }
        };
    }

    public async Task<AnimeListResponse> GetTopAnimeFromBangumiAsync(
        string? bangumiToken = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Bangumi public API doesn't require authentication
        if (!string.IsNullOrWhiteSpace(bangumiToken))
            _bangumiClient.SetToken(bangumiToken);
        _logger.LogInformation("Fetching top {Limit} anime from Bangumi", limit);

        try
        {
            var topSubjects = await _bangumiClient.SearchTopSubjectsAsync(limit);

            var animeDtos = new List<AnimeInfoDto>();

            foreach (var subject in topSubjects.EnumerateArray())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var id = subject.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                if (id == 0) continue;

                var name = subject.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var nameCn = subject.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";
                var summary = subject.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";

                var score = subject.TryGetProperty("score", out var scoreEl)
                    ? scoreEl.GetDouble().ToString("F1")
                    : "0";

                var imageUrl = subject.TryGetProperty("images", out var images) &&
                              images.TryGetProperty("large", out var large)
                              ? large.GetString() ?? ""
                              : "";

                animeDtos.Add(new AnimeInfoDto
                {
                    BangumiId = id.ToString(),
                    JpTitle = name,
                    ChTitle = nameCn,
                    EnTitle = "",
                    ChDesc = string.IsNullOrEmpty(summary) ? "无可用中文介绍" : summary,
                    EnDesc = "No English description available",
                    Score = score,
                    Images = new AnimeImagesDto
                    {
                        Portrait = imageUrl,
                        Landscape = ""
                    },
                    ExternalUrls = new ExternalUrlsDto
                    {
                        Bangumi = $"https://bgm.tv/subject/{id}",
                        Tmdb = "",
                        Anilist = ""
                    }
                });
            }

            _logger.LogInformation("Retrieved {Count} top anime from Bangumi", animeDtos.Count);

            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} anime from Bangumi",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top anime from Bangumi");
            return new AnimeListResponse
            {
                Success = false,
                DataSource = DataSource.Api,
                IsStale = true,
                Message = $"Failed to fetch Bangumi top anime: {ex.Message}",
                LastUpdated = null,
                Count = 0,
                Animes = new List<AnimeInfoDto>(),
                RetryAttempts = 0
            };
        }
    }

    public async Task<AnimeListResponse> GetTopAnimeFromAniListAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching top {Limit} trending anime from AniList", limit);

        try
        {
            var trendingAnime = await _aniListClient.GetTrendingAnimeAsync(limit);

            var animeDtos = trendingAnime.Select(anime => new AnimeInfoDto
            {
                BangumiId = "", // AniList doesn't have Bangumi ID
                JpTitle = anime.NativeTitle,
                ChTitle = "",
                EnTitle = anime.EnglishTitle,
                ChDesc = "无可用中文介绍",
                EnDesc = string.IsNullOrEmpty(anime.EnglishSummary)
                    ? "No English description available"
                    : StripHtmlTags(anime.EnglishSummary),
                Score = anime.Score,
                Images = new AnimeImagesDto
                {
                    Portrait = anime.CoverUrl,
                    Landscape = anime.BannerImage
                },
                ExternalUrls = new ExternalUrlsDto
                {
                    Bangumi = "",
                    Tmdb = "",
                    Anilist = anime.OriSiteUrl
                }
            }).ToList();

            _logger.LogInformation("Retrieved {Count} trending anime from AniList", animeDtos.Count);

            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} trending anime from AniList",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch trending anime from AniList");
            return new AnimeListResponse
            {
                Success = false,
                DataSource = DataSource.Api,
                IsStale = true,
                Message = $"Failed to fetch AniList trending anime: {ex.Message}",
                LastUpdated = null,
                Count = 0,
                Animes = new List<AnimeInfoDto>(),
                RetryAttempts = 0
            };
        }
    }

    public async Task<AnimeListResponse> GetTopAnimeFromMALAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching top {Limit} anime from MAL via Jikan", limit);

        try
        {
            var topAnime = await _jikanClient.GetTopAnimeAsync(limit);

            var animeDtos = topAnime.Select(anime => new AnimeInfoDto
            {
                BangumiId = "", // MAL doesn't have Bangumi ID
                JpTitle = anime.TitleJapanese ?? "",
                ChTitle = "",
                EnTitle = anime.TitleEnglish ?? anime.Title,
                ChDesc = "无可用中文介绍",
                EnDesc = string.IsNullOrEmpty(anime.Synopsis)
                    ? "No English description available"
                    : anime.Synopsis,
                Score = anime.Score?.ToString("F1") ?? "0",
                Images = new AnimeImagesDto
                {
                    Portrait = anime.Images?.Jpg?.LargeImageUrl ?? anime.Images?.Jpg?.ImageUrl ?? "",
                    Landscape = ""
                },
                ExternalUrls = new ExternalUrlsDto
                {
                    Bangumi = "",
                    Tmdb = "",
                    Anilist = "",
                    Mal = anime.Url
                }
            }).ToList();

            _logger.LogInformation("Retrieved {Count} top anime from MAL", animeDtos.Count);

            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} anime from MyAnimeList",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top anime from MAL");
            return new AnimeListResponse
            {
                Success = false,
                DataSource = DataSource.Api,
                IsStale = true,
                Message = $"Failed to fetch MAL top anime: {ex.Message}",
                LastUpdated = null,
                Count = 0,
                Animes = new List<AnimeInfoDto>(),
                RetryAttempts = 0
            };
        }
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Simple regex to strip HTML tags
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
    }
}
