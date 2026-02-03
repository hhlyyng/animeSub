using System.Text.Json;
using backend.Models;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

/// <summary>
/// Service that aggregates anime data from multiple external APIs
/// with Polly retry, two-tier caching, and data source tracking
/// </summary>
public class AnimeAggregationService : IAnimeAggregationService
{
    private readonly IBangumiClient _bangumiClient;
    private readonly ITMDBClient _tmdbClient;
    private readonly IAniListClient _aniListClient;
    private readonly IAnimeCacheService _cacheService;
    private readonly IResilienceService _resilienceService;
    private readonly ILogger<AnimeAggregationService> _logger;

    public AnimeAggregationService(
        IBangumiClient bangumiClient,
        ITMDBClient tmdbClient,
        IAniListClient aniListClient,
        IAnimeCacheService cacheService,
        IResilienceService resilienceService,
        ILogger<AnimeAggregationService> logger)
    {
        _bangumiClient = bangumiClient ?? throw new ArgumentNullException(nameof(bangumiClient));
        _tmdbClient = tmdbClient ?? throw new ArgumentNullException(nameof(tmdbClient));
        _aniListClient = aniListClient ?? throw new ArgumentNullException(nameof(aniListClient));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AnimeListResponse> GetTodayAnimeEnrichedAsync(
        string bangumiToken,
        string? tmdbToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bangumiToken))
            throw new ArgumentException("Bangumi token is required", nameof(bangumiToken));

        // Set tokens on clients
        _bangumiClient.SetToken(bangumiToken);
        _tmdbClient.SetToken(tmdbToken);

        _logger.LogInformation("Starting anime aggregation for today's schedule");

        // Step 1: Check if we have fresh cached data (same day)
        var cachedList = await _cacheService.GetCachedAnimeListAsync();
        var cacheTime = await _cacheService.GetTodayScheduleCacheTimeAsync();

        if (cachedList != null && cachedList.Count > 0 && cacheTime.HasValue)
        {
            // Data is from today and already cached
            _logger.LogInformation("Returning cached anime list ({Count} items, cached at {CacheTime})",
                cachedList.Count, cacheTime.Value);

            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Cache,
                IsStale = false,
                Message = "Data from cache (up to date)",
                LastUpdated = cacheTime.Value,
                Count = cachedList.Count,
                Animes = cachedList,
                RetryAttempts = 0
            };
        }

        // Step 2: Try to fetch from Bangumi API with retry
        var (bangumiData, retryCount, apiSuccess) = await _resilienceService.ExecuteWithRetryAndMetadataAsync(
            async ct => await _bangumiClient.GetDailyBroadcastAsync(),
            "Bangumi.GetDailyBroadcast",
            cancellationToken);

        // Step 3: If API failed, return cached fallback
        if (!apiSuccess || bangumiData.ValueKind == JsonValueKind.Undefined)
        {
            _logger.LogWarning("Bangumi API failed after {RetryCount} retries, using cached fallback", retryCount);

            if (cachedList != null && cachedList.Count > 0)
            {
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"API request failed after {retryCount} retries. Showing cached data.",
                    LastUpdated = cacheTime,
                    Count = cachedList.Count,
                    Animes = cachedList,
                    RetryAttempts = retryCount
                };
            }

            // No cache available at all
            return new AnimeListResponse
            {
                Success = false,
                DataSource = DataSource.Api,
                IsStale = true,
                Message = $"API request failed after {retryCount} retries and no cached data available.",
                LastUpdated = null,
                Count = 0,
                Animes = new List<object>(),
                RetryAttempts = retryCount
            };
        }

        // Step 4: Process API data and build enriched list
        var enrichedAnimes = new List<object>();

        try
        {
            foreach (var anime in bangumiData.EnumerateArray())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var enrichedAnime = await ProcessSingleAnimeAsync(anime, tmdbToken, cancellationToken);
                    if (enrichedAnime != null)
                    {
                        enrichedAnimes.Add(enrichedAnime);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing individual anime");
                    // Continue with next anime
                }
            }

            // Step 5: Cache the results
            if (enrichedAnimes.Count > 0)
            {
                // Extract bangumi IDs for caching
                var bangumiIds = new List<int>();
                foreach (var anime in enrichedAnimes)
                {
                    var type = anime.GetType();
                    var prop = type.GetProperty("bangumi_id");
                    if (prop != null)
                    {
                        var idStr = prop.GetValue(anime)?.ToString();
                        if (int.TryParse(idStr, out var id))
                        {
                            bangumiIds.Add(id);
                        }
                    }
                }

                await _cacheService.CacheTodayScheduleAsync(bangumiIds);
                await _cacheService.CacheAnimeListAsync(enrichedAnimes);
            }

            _logger.LogInformation("Anime aggregation completed successfully with {Count} anime", enrichedAnimes.Count);

            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = retryCount > 0
                    ? $"Data refreshed from API (succeeded after {retryCount} retries)"
                    : "Data refreshed from API",
                LastUpdated = DateTime.UtcNow,
                Count = enrichedAnimes.Count,
                Animes = enrichedAnimes,
                RetryAttempts = retryCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anime aggregation");

            // Try to return cached fallback on error
            if (cachedList != null && cachedList.Count > 0)
            {
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"Error processing API data: {ex.Message}. Showing cached data.",
                    LastUpdated = cacheTime,
                    Count = cachedList.Count,
                    Animes = cachedList,
                    RetryAttempts = retryCount
                };
            }

            throw;
        }
    }

    private async Task<object?> ProcessSingleAnimeAsync(
        JsonElement anime,
        string? tmdbToken,
        CancellationToken cancellationToken)
    {
        var bangumiId = anime.GetProperty("id").GetInt32();

        var oriTitle = anime.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind != JsonValueKind.Null
            ? nameProperty.GetString() ?? ""
            : "";

        bool containsJapaneseInOriTitle = !string.IsNullOrEmpty(oriTitle) &&
            System.Text.RegularExpressions.Regex.IsMatch(oriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

        bool containsPureChineseInOriTitle = !string.IsNullOrEmpty(oriTitle) &&
            System.Text.RegularExpressions.Regex.IsMatch(oriTitle, @"^[\p{IsCJKUnifiedIdeographs}]+$") &&
            !System.Text.RegularExpressions.Regex.IsMatch(oriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

        var chTitle = anime.TryGetProperty("name_cn", out var nameCn) && nameCn.ValueKind != JsonValueKind.Null
            ? nameCn.GetString() ?? ""
            : "";

        var chDesc = anime.TryGetProperty("summary", out var summary) && summary.ValueKind != JsonValueKind.Null
            ? summary.GetString() ?? ""
            : "";

        var score = anime.TryGetProperty("rating", out var rating) && rating.ValueKind != JsonValueKind.Null &&
                rating.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind != JsonValueKind.Null
                ? scoreEl.GetDouble().ToString("F1")
                : "0";

        _logger.LogInformation("Processing anime: {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);

        // Check cache for images first
        var cachedImages = await _cacheService.GetAnimeImagesCachedAsync(bangumiId);

        Models.TMDBAnimeInfo? tmdbResult = null;
        string? backdropUrl = cachedImages?.BackdropUrl;

        // Only fetch from TMDB if not cached
        if (cachedImages == null || string.IsNullOrEmpty(cachedImages.BackdropUrl))
        {
            tmdbResult = await FetchTmdbDataAsync(oriTitle, cancellationToken);

            // Cache the images if fetched successfully
            if (tmdbResult != null)
            {
                var posterUrl = anime.TryGetProperty("images", out var imgProp) && imgProp.ValueKind != JsonValueKind.Null &&
                              imgProp.TryGetProperty("large", out var largeImage) && largeImage.ValueKind != JsonValueKind.Null
                              ? largeImage.GetString()
                              : null;

                await _cacheService.CacheAnimeImagesAsync(
                    bangumiId,
                    posterUrl,
                    tmdbResult.BackdropUrl,
                    null);

                backdropUrl = tmdbResult.BackdropUrl;
                _logger.LogInformation("Cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
            }
        }
        else
        {
            _logger.LogInformation("Using cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
        }

        // Fetch AniList data
        var anilistResult = await FetchAniListDataAsync(oriTitle, cancellationToken);

        // Build enriched anime object
        return new
        {
            bangumi_id = bangumiId.ToString(),
            jp_title = containsJapaneseInOriTitle ? oriTitle : "",
            ch_title = containsPureChineseInOriTitle ? oriTitle : chTitle,
            en_title = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "",
            ch_desc = chDesc,
            en_desc = tmdbResult?.EnglishSummary ?? anilistResult?.EnglishSummary ?? "",
            score = score,
            images = new
            {
                portrait = anime.TryGetProperty("images", out var images) && images.ValueKind != JsonValueKind.Null &&
                        images.TryGetProperty("large", out var large) && large.ValueKind != JsonValueKind.Null
                        ? large.GetString() ?? ""
                        : "",
                landscape = backdropUrl ?? ""
            },
            external_urls = new
            {
                bangumi = $"https://bgm.tv/subject/{bangumiId}",
                tmdb = tmdbResult?.OriSiteUrl ?? "",
                anilist = anilistResult?.OriSiteUrl ?? ""
            }
        };
    }

    private async Task<Models.TMDBAnimeInfo?> FetchTmdbDataAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdbClient.GetAnimeSummaryAndBackdropAsync(title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB fetch failed for '{Title}', continuing with AniList", title);
            return null;
        }
    }

    private async Task<Models.AniListAnimeInfo?> FetchAniListDataAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            return await _aniListClient.GetAnimeInfoAsync(title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AniList fetch failed for '{Title}'", title);
            return null;
        }
    }
}
