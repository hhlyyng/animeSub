using System.Text.Json;
using backend.Models;
using backend.Models.Dtos;
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
                Animes = new List<AnimeInfoDto>(),
                RetryAttempts = retryCount
            };
        }

        // Step 4: Process API data and build enriched list
        var enrichedAnimes = new List<AnimeInfoDto>();

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
                // Extract bangumi IDs for caching (now strongly typed)
                var bangumiIds = enrichedAnimes
                    .Where(a => int.TryParse(a.BangumiId, out _))
                    .Select(a => int.Parse(a.BangumiId))
                    .ToList();

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

    private async Task<AnimeInfoDto?> ProcessSingleAnimeAsync(
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

        // Get air date for TMDB season matching (format: YYYY-MM-DD)
        var airDate = anime.TryGetProperty("date", out var dateEl) && dateEl.ValueKind != JsonValueKind.Null
            ? dateEl.GetString()
            : null;

        _logger.LogInformation("Processing anime: {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);

        // Fetch full subject detail if summary is empty (calendar API doesn't include summary)
        if (string.IsNullOrEmpty(chDesc) || string.IsNullOrEmpty(chTitle) || string.IsNullOrEmpty(airDate))
        {
            try
            {
                var subjectDetail = await _bangumiClient.GetSubjectDetailAsync(bangumiId);

                if (string.IsNullOrEmpty(chDesc) &&
                    subjectDetail.TryGetProperty("summary", out var detailSummary) &&
                    detailSummary.ValueKind != JsonValueKind.Null)
                {
                    chDesc = detailSummary.GetString() ?? "";
                    _logger.LogInformation("Fetched Chinese description from subject detail for {Title}", oriTitle);
                }

                if (string.IsNullOrEmpty(chTitle) &&
                    subjectDetail.TryGetProperty("name_cn", out var detailNameCn) &&
                    detailNameCn.ValueKind != JsonValueKind.Null)
                {
                    chTitle = detailNameCn.GetString() ?? "";
                    _logger.LogInformation("Fetched Chinese title from subject detail for {Title}", oriTitle);
                }

                if (string.IsNullOrEmpty(airDate) &&
                    subjectDetail.TryGetProperty("date", out var detailDate) &&
                    detailDate.ValueKind != JsonValueKind.Null)
                {
                    airDate = detailDate.GetString();
                    _logger.LogInformation("Fetched air date from subject detail for {Title}: {AirDate}", oriTitle, airDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch subject detail for {BangumiId}, continuing without Chinese description", bangumiId);
            }
        }

        // Check cache for images first
        var cachedImages = await _cacheService.GetAnimeImagesCachedAsync(bangumiId);

        Models.TMDBAnimeInfo? tmdbResult = null;
        string? backdropUrl = cachedImages?.BackdropUrl;

        // Fetch TMDB and AniList in parallel for better performance
        var needTmdbFetch = cachedImages == null || string.IsNullOrEmpty(cachedImages.BackdropUrl);

        var tmdbTask = needTmdbFetch
            ? FetchTmdbDataAsync(oriTitle, airDate, cancellationToken)
            : Task.FromResult<Models.TMDBAnimeInfo?>(null);
        var anilistTask = FetchAniListDataAsync(oriTitle, cancellationToken);

        // Wait for both to complete (parallel execution)
        await Task.WhenAll(tmdbTask, anilistTask).ConfigureAwait(false);

        tmdbResult = await tmdbTask;
        var anilistResult = await anilistTask;

        // Cache TMDB images if fetched successfully
        if (needTmdbFetch && tmdbResult != null)
        {
            var posterUrl = anime.TryGetProperty("images", out var imgProp) && imgProp.ValueKind != JsonValueKind.Null &&
                          imgProp.TryGetProperty("large", out var largeImage) && largeImage.ValueKind != JsonValueKind.Null
                          ? largeImage.GetString()
                          : null;

            await _cacheService.CacheAnimeImagesAsync(
                bangumiId,
                posterUrl,
                tmdbResult.BackdropUrl,
                null).ConfigureAwait(false);

            backdropUrl = tmdbResult.BackdropUrl;
            _logger.LogInformation("Cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
        }
        else if (!needTmdbFetch)
        {
            _logger.LogInformation("Using cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
        }

        // Build enriched anime DTO (strongly typed)
        var portraitUrl = anime.TryGetProperty("images", out var images) && images.ValueKind != JsonValueKind.Null &&
                images.TryGetProperty("large", out var large) && large.ValueKind != JsonValueKind.Null
                ? large.GetString() ?? ""
                : "";

        // Determine final titles with fallback
        var jpTitle = containsJapaneseInOriTitle ? oriTitle : "";
        var finalChTitle = containsPureChineseInOriTitle ? oriTitle : chTitle;
        var finalEnTitle = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "";

        // Skip anime if no valid title available
        if (string.IsNullOrEmpty(jpTitle) && string.IsNullOrEmpty(finalChTitle) && string.IsNullOrEmpty(finalEnTitle))
        {
            _logger.LogWarning("Skipping anime (BangumiId: {BangumiId}) - no valid title available", bangumiId);
            return null;
        }

        // Check if Bangumi summary contains Japanese (hiragana/katakana)
        // If so, it's not a valid Chinese description
        bool bangumiDescIsJapanese = !string.IsNullOrEmpty(chDesc) &&
            System.Text.RegularExpressions.Regex.IsMatch(chDesc, @"[\p{IsHiragana}\p{IsKatakana}]");

        // Build Chinese description with fallback chain
        string finalChDesc;
        if (!string.IsNullOrEmpty(chDesc) && !bangumiDescIsJapanese)
        {
            // Bangumi has valid Chinese description
            finalChDesc = chDesc;
        }
        else if (!string.IsNullOrEmpty(tmdbResult?.ChineseSummary))
        {
            // Fallback to TMDB Chinese
            finalChDesc = tmdbResult.ChineseSummary;
        }
        else
        {
            // No Chinese description available
            finalChDesc = "无可用中文介绍";
        }

        // Build English description with fallback chain
        string finalEnDesc;
        if (!string.IsNullOrEmpty(tmdbResult?.EnglishSummary))
        {
            finalEnDesc = tmdbResult.EnglishSummary;
        }
        else if (!string.IsNullOrEmpty(anilistResult?.EnglishSummary))
        {
            finalEnDesc = anilistResult.EnglishSummary;
        }
        else
        {
            // No English description available
            finalEnDesc = "No English description available";
        }

        return new AnimeInfoDto
        {
            BangumiId = bangumiId.ToString(),
            JpTitle = jpTitle,
            ChTitle = finalChTitle,
            EnTitle = finalEnTitle,
            ChDesc = finalChDesc,
            EnDesc = finalEnDesc,
            Score = score,
            Images = new AnimeImagesDto
            {
                Portrait = portraitUrl,
                Landscape = backdropUrl ?? ""
            },
            ExternalUrls = new ExternalUrlsDto
            {
                Bangumi = $"https://bgm.tv/subject/{bangumiId}",
                Tmdb = tmdbResult?.OriSiteUrl ?? "",
                Anilist = anilistResult?.OriSiteUrl ?? ""
            }
        };
    }

    private async Task<Models.TMDBAnimeInfo?> FetchTmdbDataAsync(string title, string? airDate, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdbClient.GetAnimeSummaryAndBackdropAsync(title, airDate);
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
