using System.Text.Json;
using backend.Services.Interfaces;

namespace backend.Services.Implementations
{

/// <summary>
/// Service that aggregates anime data from multiple external APIs
/// with two-tier caching (IMemoryCache + SQLite)
/// </summary>
public class AnimeAggregationService : IAnimeAggregationService
{
    private readonly IBangumiClient _bangumiClient;
    private readonly ITMDBClient _tmdbClient;
    private readonly IAniListClient _aniListClient;
    private readonly IAnimeCacheService _cacheService;
    private readonly ILogger<AnimeAggregationService> _logger;

    public AnimeAggregationService(
        IBangumiClient bangumiClient,
        ITMDBClient tmdbClient,
        IAniListClient aniListClient,
        IAnimeCacheService cacheService,
        ILogger<AnimeAggregationService> logger)
    {
        _bangumiClient = bangumiClient ?? throw new ArgumentNullException(nameof(bangumiClient));
        _tmdbClient = tmdbClient ?? throw new ArgumentNullException(nameof(tmdbClient));
        _aniListClient = aniListClient ?? throw new ArgumentNullException(nameof(aniListClient));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<object>> GetTodayAnimeEnrichedAsync(
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

        var enrichedAnimes = new List<object>();

        try
        {
            // Get Bangumi data
            var bangumiData = await _bangumiClient.GetDailyBroadcastAsync().ConfigureAwait(false);

            foreach (var anime in bangumiData.EnumerateArray())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
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
                        tmdbResult = await FetchTmdbDataAsync(oriTitle, cancellationToken).ConfigureAwait(false);

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
                                null); // TMDB ID can be extracted if needed

                            backdropUrl = tmdbResult.BackdropUrl;
                            _logger.LogInformation("Cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Using cached images for {Title} (BangumiId: {BangumiId})", oriTitle, bangumiId);
                    }

                    // Fetch AniList data (always fetch as it's lightweight and provides English metadata)
                    var anilistResult = await FetchAniListDataAsync(oriTitle, cancellationToken).ConfigureAwait(false);

                    // Build enriched anime object
                    enrichedAnimes.Add(new
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
                    });

                    _logger.LogInformation("Successfully processed: {Title}", oriTitle);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing individual anime");
                    // Continue with next anime
                }
            }

            _logger.LogInformation("Anime aggregation completed successfully with {Count} anime", enrichedAnimes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anime aggregation");
            throw;
        }

        return enrichedAnimes;
    }

    private async Task<Models.TMDBAnimeInfo?> FetchTmdbDataAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdbClient.GetAnimeSummaryAndBackdropAsync(title).ConfigureAwait(false);
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
            return await _aniListClient.GetAnimeInfoAsync(title).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AniList fetch failed for '{Title}'", title);
            return null;
        }
    }
}
}
