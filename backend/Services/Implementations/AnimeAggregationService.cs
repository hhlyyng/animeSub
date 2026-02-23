using System.Text.Json;
using System.Text;
using backend.Data.Entities;
using backend.Models;
using backend.Models.Dtos;
using backend.Models.Jikan;
using backend.Services.Interfaces;
using backend.Services.Repositories;
using backend.Services.Utilities;

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

    private const string TopBangumiCacheSource = "top:bangumi";
    private const string TopAniListCacheSource = "top:anilist";
    private const string TopMalCacheSource = "top:mal";
    private static readonly TimeSpan TopListCacheTtl = TimeSpan.FromHours(24);

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
        string? tmdbToken = null,
        CancellationToken cancellationToken = default)
    {
        // Set TMDB token (if provided)
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

                // Step 3: Enrich anime missing landscape images (if TMDB token available)
                var animesNeedingLandscape = preFetchedAnimes.Where(a => string.IsNullOrEmpty(a.ImageLandscape)).ToList();
                if (animesNeedingLandscape.Count > 0)
                {
                    _logger.LogInformation("Enriching {Count} anime with missing landscape images", animesNeedingLandscape.Count);
                    await EnrichMissingLandscapesAsync(animesNeedingLandscape, cancellationToken);
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
            return await FetchAllFromApiAsync(tmdbToken, todayWeekday, cancellationToken);
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
        var enTitle = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "";
        var resolvedTitles = TitleLanguageResolver.ResolveFromName(
            oriTitle,
            jpTitle: null,
            chTitle: chTitle,
            enTitle: enTitle);
        var jpTitle = resolvedTitles.jpTitle;
        chTitle = resolvedTitles.chTitle;
        enTitle = resolvedTitles.enTitle;

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

        // Determine landscape image: TMDB backdrop > AniList banner
        var landscapeUrl = !string.IsNullOrEmpty(tmdbResult?.BackdropUrl)
            ? tmdbResult.BackdropUrl
            : (anilistResult?.BannerImage ?? "");

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
            ImageLandscape = landscapeUrl,
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
            },
            MikanBangumiId = entity.MikanBangumiId
        };
    }

    public async Task<AnimeListResponse> GetTopAnimeFromBangumiAsync(
        string? tmdbToken = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Set TMDB token for enrichment
        _tmdbClient.SetToken(tmdbToken);
        _logger.LogInformation("Fetching top {Limit} anime from Bangumi with enrichment", limit);

        var (cachedTopBangumi, cachedTopBangumiUpdatedAt) = await TryReadTopListCacheAsync(
            TopBangumiCacheSource,
            allowStale: false);
        if (cachedTopBangumi != null && cachedTopBangumi.Count > 0)
        {
            _logger.LogInformation("Returning Bangumi top list from persistent cache ({Count})", cachedTopBangumi.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Database,
                IsStale = false,
                Message = $"Top {cachedTopBangumi.Count} anime from Bangumi (cached)",
                LastUpdated = cachedTopBangumiUpdatedAt,
                Count = cachedTopBangumi.Count,
                Animes = cachedTopBangumi,
                RetryAttempts = 0
            };
        }

        try
        {
            var topSubjects = await _bangumiClient.SearchTopSubjectsAsync(limit);
            var subjectList = topSubjects.EnumerateArray().ToList();
            var topIds = subjectList
                .Select(subject => subject.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var cachedById = (await _repository.GetAnimeInfoBatchAsync(topIds))
                .ToDictionary(a => a.BangumiId, a => a);

            var animeDtos = new List<AnimeInfoDto>();

            foreach (var subject in subjectList)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var id = subject.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                if (id == 0) continue;

                cachedById.TryGetValue(id, out var cachedAnime);

                var nameTitle = subject.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

                var chTitle = subject.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(chTitle))
                {
                    chTitle = cachedAnime?.NameChinese ?? "";
                }

                var enTitle = cachedAnime?.NameEnglish ?? "";
                var resolvedTitles = TitleLanguageResolver.ResolveFromName(
                    nameTitle,
                    jpTitle: cachedAnime?.NameJapanese,
                    chTitle: chTitle,
                    enTitle: enTitle);
                var jpTitle = resolvedTitles.jpTitle;
                chTitle = resolvedTitles.chTitle;
                enTitle = resolvedTitles.enTitle;

                var titleForEnrichment = !string.IsNullOrWhiteSpace(nameTitle)
                    ? nameTitle
                    : (!string.IsNullOrWhiteSpace(jpTitle)
                        ? jpTitle
                        : (!string.IsNullOrWhiteSpace(chTitle) ? chTitle : enTitle));

                var chDesc = subject.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(chDesc))
                {
                    chDesc = cachedAnime?.DescChinese ?? "";
                }
                var airDate = subject.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(airDate))
                {
                    airDate = cachedAnime?.AirDate;
                }

                // Get score from rating object
                var score = cachedAnime?.Score ?? "0";
                if (subject.TryGetProperty("rating", out var rating) &&
                    rating.TryGetProperty("score", out var scoreEl))
                {
                    score = scoreEl.GetDouble().ToString("F1");
                }

                var portraitUrl = subject.TryGetProperty("images", out var images) &&
                                  images.TryGetProperty("large", out var large)
                                  ? large.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(portraitUrl))
                {
                    portraitUrl = cachedAnime?.ImagePortrait ?? "";
                }

                var enDesc = cachedAnime?.DescEnglish ?? "";
                var landscapeUrl = cachedAnime?.ImageLandscape ?? "";
                var tmdbUrl = cachedAnime?.UrlTmdb ?? "";
                var anilistUrl = cachedAnime?.UrlAnilist ?? "";

                if (string.IsNullOrWhiteSpace(landscapeUrl) ||
                    string.IsNullOrWhiteSpace(enTitle) ||
                    string.IsNullOrWhiteSpace(enDesc) ||
                    string.IsNullOrWhiteSpace(tmdbUrl))
                {
                    var tmdbResult = await EnrichWithTmdbAsync(titleForEnrichment, airDate);
                    if (string.IsNullOrWhiteSpace(enTitle))
                    {
                        enTitle = tmdbResult.enTitle;
                    }
                    if (string.IsNullOrWhiteSpace(enDesc))
                    {
                        enDesc = tmdbResult.enDesc;
                    }
                    if (string.IsNullOrWhiteSpace(landscapeUrl))
                    {
                        landscapeUrl = tmdbResult.landscapeUrl;
                    }
                    if (string.IsNullOrWhiteSpace(tmdbUrl))
                    {
                        tmdbUrl = tmdbResult.tmdbUrl;
                    }
                }

                if (string.IsNullOrEmpty(landscapeUrl))
                {
                    var anilistData = await EnrichWithAniListAsync(titleForEnrichment);
                    if (anilistData != null)
                    {
                        landscapeUrl = anilistData.BannerImage ?? "";
                        if (string.IsNullOrWhiteSpace(anilistUrl))
                        {
                            anilistUrl = anilistData.OriSiteUrl ?? "";
                        }
                        if (string.IsNullOrEmpty(enTitle)) enTitle = anilistData.EnglishTitle ?? "";
                        if (string.IsNullOrEmpty(enDesc)) enDesc = StripHtmlTags(anilistData.EnglishSummary ?? "");
                    }
                }

                animeDtos.Add(new AnimeInfoDto
                {
                    BangumiId = id.ToString(),
                    JpTitle = jpTitle,
                    ChTitle = chTitle,
                    EnTitle = enTitle,
                    ChDesc = string.IsNullOrEmpty(chDesc) ? "无可用中文介绍" : chDesc,
                    EnDesc = string.IsNullOrEmpty(enDesc) ? "No English description available" : enDesc,
                    Score = score,
                    Images = new AnimeImagesDto { Portrait = portraitUrl, Landscape = landscapeUrl },
                    ExternalUrls = new ExternalUrlsDto
                    {
                        Bangumi = $"https://bgm.tv/subject/{id}",
                        Tmdb = tmdbUrl,
                        Anilist = anilistUrl
                    }
                });

                await PersistTopAnimeCacheAsync(
                    existing: cachedAnime,
                    bangumiId: id,
                    jpTitle: jpTitle,
                    chTitle: chTitle,
                    enTitle: enTitle,
                    chDesc: chDesc,
                    enDesc: enDesc,
                    score: score,
                    portraitUrl: portraitUrl,
                    landscapeUrl: landscapeUrl,
                    airDate: airDate,
                    urlBangumi: $"https://bgm.tv/subject/{id}",
                    urlTmdb: tmdbUrl,
                    urlAnilist: anilistUrl);
            }

            await SaveTopListCacheAsync(TopBangumiCacheSource, animeDtos);

            _logger.LogInformation("Retrieved {Count} top anime from Bangumi (enriched)", animeDtos.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} anime from Bangumi (enriched)",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top anime from Bangumi");

            var (fallbackTopBangumi, fallbackTopBangumiUpdatedAt) = await TryReadTopListCacheAsync(
                TopBangumiCacheSource,
                allowStale: true);
            if (fallbackTopBangumi != null && fallbackTopBangumi.Count > 0)
            {
                _logger.LogWarning(
                    "Using stale Bangumi top cache fallback due to API failure. Cached count={Count}",
                    fallbackTopBangumi.Count);
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"Failed to refresh Bangumi top anime, using cached snapshot: {ex.Message}",
                    LastUpdated = fallbackTopBangumiUpdatedAt,
                    Count = fallbackTopBangumi.Count,
                    Animes = fallbackTopBangumi,
                    RetryAttempts = 0
                };
            }

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
        string? tmdbToken = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Set TMDB token for backdrop enrichment
        _tmdbClient.SetToken(tmdbToken);
        _logger.LogInformation("Fetching top {Limit} trending anime from AniList with enrichment", limit);

        var (cachedTopAniList, cachedTopAniListUpdatedAt) = await TryReadTopListCacheAsync(
            TopAniListCacheSource,
            allowStale: false);
        if (cachedTopAniList != null && cachedTopAniList.Count > 0)
        {
            _logger.LogInformation("Returning AniList top list from persistent cache ({Count})", cachedTopAniList.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Database,
                IsStale = false,
                Message = $"Top {cachedTopAniList.Count} trending anime from AniList (cached)",
                LastUpdated = cachedTopAniListUpdatedAt,
                Count = cachedTopAniList.Count,
                Animes = cachedTopAniList,
                RetryAttempts = 0
            };
        }

        try
        {
            var trendingAnime = await _aniListClient.GetTrendingAnimeAsync(limit);
            var animeDtos = new List<AnimeInfoDto>();

            foreach (var anime in trendingAnime)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var jpTitle = anime.NativeTitle ?? "";
                var enTitle = anime.EnglishTitle ?? "";
                var enDesc = StripHtmlTags(anime.EnglishSummary ?? "");

                // Enrich with Bangumi (Chinese data)
                var (bangumiId, chTitle, chDesc, bangumiUrl) = await EnrichWithBangumiAsync(jpTitle, enTitle);

                // PRIORITY: Try TMDB first for better quality backdrop
                var landscapeUrl = "";
                var tmdbUrl = "";
                var (_, _, tmdbLandscape, tmdbUrlResult) = await EnrichWithTmdbAsync(jpTitle, null);
                if (!string.IsNullOrEmpty(tmdbLandscape))
                {
                    landscapeUrl = tmdbLandscape;
                    tmdbUrl = tmdbUrlResult;
                }
                else
                {
                    // Fallback to AniList banner if TMDB has no backdrop
                    landscapeUrl = anime.BannerImage ?? "";
                }

                animeDtos.Add(new AnimeInfoDto
                {
                    BangumiId = bangumiId,
                    JpTitle = jpTitle,
                    ChTitle = chTitle,
                    EnTitle = enTitle,
                    ChDesc = string.IsNullOrEmpty(chDesc) ? "无可用中文介绍" : chDesc,
                    EnDesc = string.IsNullOrEmpty(enDesc) ? "No English description available" : enDesc,
                    Score = anime.Score ?? "0",
                    Images = new AnimeImagesDto { Portrait = anime.CoverUrl ?? "", Landscape = landscapeUrl },
                    ExternalUrls = new ExternalUrlsDto
                    {
                        Bangumi = bangumiUrl,
                        Tmdb = tmdbUrl,
                        Anilist = anime.OriSiteUrl ?? ""
                    }
                });
            }

            await SaveTopListCacheAsync(TopAniListCacheSource, animeDtos);

            _logger.LogInformation("Retrieved {Count} trending anime from AniList (enriched)", animeDtos.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} trending anime from AniList (enriched)",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch trending anime from AniList");

            var (fallbackTopAniList, fallbackTopAniListUpdatedAt) = await TryReadTopListCacheAsync(
                TopAniListCacheSource,
                allowStale: true);
            if (fallbackTopAniList != null && fallbackTopAniList.Count > 0)
            {
                _logger.LogWarning(
                    "Using stale AniList top cache fallback due to API failure. Cached count={Count}",
                    fallbackTopAniList.Count);
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"Failed to refresh AniList trending anime, using cached snapshot: {ex.Message}",
                    LastUpdated = fallbackTopAniListUpdatedAt,
                    Count = fallbackTopAniList.Count,
                    Animes = fallbackTopAniList,
                    RetryAttempts = 0
                };
            }

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
        string? tmdbToken = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Set TMDB token for backdrop enrichment
        _tmdbClient.SetToken(tmdbToken);
        _logger.LogInformation("Fetching top {Limit} anime from MAL via Jikan with enrichment", limit);

        var (cachedTopMal, cachedTopMalUpdatedAt) = await TryReadTopListCacheAsync(
            TopMalCacheSource,
            allowStale: false);
        if (cachedTopMal != null && cachedTopMal.Count > 0)
        {
            _logger.LogInformation("Returning MAL top list from persistent cache ({Count})", cachedTopMal.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Database,
                IsStale = false,
                Message = $"Top {cachedTopMal.Count} anime from MyAnimeList (cached)",
                LastUpdated = cachedTopMalUpdatedAt,
                Count = cachedTopMal.Count,
                Animes = cachedTopMal,
                RetryAttempts = 0
            };
        }

        try
        {
            var topAnime = await _jikanClient.GetTopAnimeAsync(limit);
            var animeDtos = new List<AnimeInfoDto>();
            var seenMalUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var anime in topAnime)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var malUrl = anime.Url?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(malUrl) && !seenMalUrls.Add(malUrl))
                {
                    _logger.LogDebug("Skipping duplicated MAL entry by URL: {MalUrl}", malUrl);
                    continue;
                }

                var jpTitle = anime.TitleJapanese ?? "";
                var enTitle = anime.TitleEnglish ?? anime.Title ?? "";
                var enDesc = anime.Synopsis ?? "";
                var portraitUrl = anime.Images?.Jpg?.LargeImageUrl ?? anime.Images?.Jpg?.ImageUrl ?? "";

                var cachedByTitle = await _repository.FindAnimeInfoByAnyTitleAsync(jpTitle, anime.Title);
                if (cachedByTitle != null &&
                    !IsCacheEntryCompatibleWithMalAnime(cachedByTitle, jpTitle, anime.Title))
                {
                    _logger.LogDebug(
                        "Ignoring MAL cache hit due to title mismatch. MAL JP='{MalJp}', MAL Title='{MalTitle}', Cached JP='{CachedJp}', Cached EN='{CachedEn}'",
                        jpTitle,
                        anime.Title,
                        cachedByTitle.NameJapanese,
                        cachedByTitle.NameEnglish);
                    cachedByTitle = null;
                }

                var bangumiId = cachedByTitle?.BangumiId.ToString() ?? "";
                var chTitle = cachedByTitle?.NameChinese ?? "";
                var chDesc = cachedByTitle?.DescChinese ?? "";
                var bangumiUrl = cachedByTitle?.UrlBangumi ?? "";

                var cachedByBangumi = cachedByTitle;
                if (string.IsNullOrWhiteSpace(bangumiId))
                {
                    var enrichedBangumi = await EnrichWithBangumiAsync(jpTitle, enTitle);
                    bangumiId = enrichedBangumi.bangumiId;
                    chTitle = string.IsNullOrWhiteSpace(chTitle) ? enrichedBangumi.chTitle : chTitle;
                    chDesc = string.IsNullOrWhiteSpace(chDesc) ? enrichedBangumi.chDesc : chDesc;
                    bangumiUrl = string.IsNullOrWhiteSpace(bangumiUrl) ? enrichedBangumi.bangumiUrl : bangumiUrl;

                    if (int.TryParse(bangumiId, out var resolvedBangumiId) && resolvedBangumiId > 0)
                    {
                        cachedByBangumi = await _repository.GetAnimeInfoAsync(resolvedBangumiId);
                    }
                }

                var clientFacingId = string.IsNullOrWhiteSpace(bangumiId)
                    ? BuildMalFallbackId(anime)
                    : bangumiId;

                // PRIORITY: Try TMDB first for better quality backdrop
                var landscapeUrl = cachedByBangumi?.ImageLandscape ?? "";
                var anilistUrl = cachedByBangumi?.UrlAnilist ?? "";
                var tmdbUrl = cachedByBangumi?.UrlTmdb ?? "";
                if (string.IsNullOrWhiteSpace(portraitUrl))
                {
                    portraitUrl = cachedByBangumi?.ImagePortrait ?? "";
                }

                if (string.IsNullOrWhiteSpace(chTitle))
                {
                    chTitle = cachedByBangumi?.NameChinese ?? "";
                }
                if (string.IsNullOrWhiteSpace(chDesc))
                {
                    chDesc = cachedByBangumi?.DescChinese ?? "";
                }

                chTitle = ApplyKnownChineseTitleOverrides(chTitle);

                if (string.IsNullOrWhiteSpace(landscapeUrl) || string.IsNullOrWhiteSpace(tmdbUrl))
                {
                    var (_, _, tmdbLandscape, tmdbUrlResult) = await EnrichWithTmdbAsync(jpTitle, null);
                    if (!string.IsNullOrEmpty(tmdbLandscape))
                    {
                        if (string.IsNullOrWhiteSpace(landscapeUrl))
                        {
                            landscapeUrl = tmdbLandscape;
                        }
                        if (string.IsNullOrWhiteSpace(tmdbUrl))
                        {
                            tmdbUrl = tmdbUrlResult;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(landscapeUrl))
                {
                    // Fallback to AniList banner if TMDB has no backdrop
                    var anilistData = await EnrichWithAniListAsync(jpTitle);
                    if (anilistData != null)
                    {
                        landscapeUrl = anilistData.BannerImage ?? "";
                        anilistUrl = anilistData.OriSiteUrl ?? "";
                    }
                }

                animeDtos.Add(new AnimeInfoDto
                {
                    BangumiId = clientFacingId,
                    JpTitle = jpTitle,
                    ChTitle = chTitle,
                    EnTitle = enTitle,
                    ChDesc = string.IsNullOrEmpty(chDesc) ? "无可用中文介绍" : chDesc,
                    EnDesc = string.IsNullOrEmpty(enDesc) ? "No English description available" : enDesc,
                    Score = anime.Score?.ToString("F1") ?? "0",
                    Images = new AnimeImagesDto { Portrait = portraitUrl, Landscape = landscapeUrl },
                    ExternalUrls = new ExternalUrlsDto
                    {
                        Bangumi = bangumiUrl,
                        Tmdb = tmdbUrl,
                        Anilist = anilistUrl,
                        Mal = malUrl
                    }
                });

                if (int.TryParse(bangumiId, out var bangumiNumericId) && bangumiNumericId > 0)
                {
                    await PersistTopAnimeCacheAsync(
                        existing: cachedByBangumi,
                        bangumiId: bangumiNumericId,
                        jpTitle: jpTitle,
                        chTitle: chTitle,
                        enTitle: enTitle,
                        chDesc: chDesc,
                        enDesc: enDesc,
                        score: anime.Score?.ToString("F1") ?? "0",
                        portraitUrl: portraitUrl,
                        landscapeUrl: landscapeUrl,
                        airDate: cachedByBangumi?.AirDate,
                        urlBangumi: string.IsNullOrWhiteSpace(bangumiUrl) ? $"https://bgm.tv/subject/{bangumiNumericId}" : bangumiUrl,
                        urlTmdb: tmdbUrl,
                        urlAnilist: anilistUrl);
                }
            }

            await SaveTopListCacheAsync(TopMalCacheSource, animeDtos);

            _logger.LogInformation("Retrieved {Count} top anime from MAL (enriched)", animeDtos.Count);
            return new AnimeListResponse
            {
                Success = true,
                DataSource = DataSource.Api,
                IsStale = false,
                Message = $"Top {animeDtos.Count} anime from MyAnimeList (enriched)",
                LastUpdated = DateTime.UtcNow,
                Count = animeDtos.Count,
                Animes = animeDtos,
                RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch top anime from MAL");

            var (fallbackTopMal, fallbackTopMalUpdatedAt) = await TryReadTopListCacheAsync(
                TopMalCacheSource,
                allowStale: true);
            if (fallbackTopMal != null && fallbackTopMal.Count > 0)
            {
                _logger.LogWarning(
                    "Using stale MAL top cache fallback due to API failure. Cached count={Count}",
                    fallbackTopMal.Count);
                return new AnimeListResponse
                {
                    Success = true,
                    DataSource = DataSource.CacheFallback,
                    IsStale = true,
                    Message = $"Failed to refresh MAL top anime, using cached snapshot: {ex.Message}",
                    LastUpdated = fallbackTopMalUpdatedAt,
                    Count = fallbackTopMal.Count,
                    Animes = fallbackTopMal,
                    RetryAttempts = 0
                };
            }

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

    public async Task<AnimeListResponse> SearchAnimeAsync(
        string query,
        string? bangumiToken = null,
        string? tmdbToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(bangumiToken))
            _bangumiClient.SetToken(bangumiToken);
        _tmdbClient.SetToken(tmdbToken);
        _logger.LogInformation("Searching anime for query: {Query}", query);

        try
        {
            var subjectList = await _bangumiClient.SearchSubjectListAsync(query);
            var animeDtos = new List<AnimeInfoDto>();

            if (subjectList.ValueKind == JsonValueKind.Array)
            {
                foreach (var subject in subjectList.EnumerateArray())
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var id = subject.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                    if (id == 0) continue;

                    var jpTitle = subject.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    var chTitle = subject.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";
                    var chDesc = subject.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";
                    var airDate = subject.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;

                    var score = "0";
                    if (subject.TryGetProperty("rating", out var rating) &&
                        rating.TryGetProperty("score", out var scoreEl))
                    {
                        score = scoreEl.GetDouble().ToString("F1");
                    }

                    var portraitUrl = subject.TryGetProperty("images", out var images) &&
                                      images.TryGetProperty("large", out var large)
                                      ? large.GetString() ?? "" : "";

                    // Enrich with TMDB (landscape + English data)
                    var (enTitle, enDesc, landscapeUrl, tmdbUrl) = await EnrichWithTmdbAsync(jpTitle, airDate);

                    // If TMDB didn't return landscape, try AniList
                    var anilistUrl = "";
                    if (string.IsNullOrEmpty(landscapeUrl))
                    {
                        var anilistData = await EnrichWithAniListAsync(jpTitle);
                        if (anilistData != null)
                        {
                            landscapeUrl = anilistData.BannerImage ?? "";
                            anilistUrl = anilistData.OriSiteUrl ?? "";
                            if (string.IsNullOrEmpty(enTitle)) enTitle = anilistData.EnglishTitle ?? "";
                            if (string.IsNullOrEmpty(enDesc)) enDesc = StripHtmlTags(anilistData.EnglishSummary ?? "");
                        }
                    }

                    animeDtos.Add(new AnimeInfoDto
                    {
                        BangumiId = id.ToString(),
                        JpTitle = jpTitle,
                        ChTitle = chTitle,
                        EnTitle = enTitle,
                        ChDesc = string.IsNullOrEmpty(chDesc) ? "无可用中文介绍" : chDesc,
                        EnDesc = string.IsNullOrEmpty(enDesc) ? "No English description available" : enDesc,
                        Score = score,
                        Images = new AnimeImagesDto { Portrait = portraitUrl, Landscape = landscapeUrl },
                        ExternalUrls = new ExternalUrlsDto
                        {
                            Bangumi = $"https://bgm.tv/subject/{id}",
                            Tmdb = tmdbUrl,
                            Anilist = anilistUrl
                        }
                    });
                }
            }

            _logger.LogInformation("Search for '{Query}' returned {Count} results (enriched)", query, animeDtos.Count);
            return new AnimeListResponse
            {
                Success = true, DataSource = DataSource.Api, IsStale = false,
                Message = $"Search results for '{query}' ({animeDtos.Count} found)",
                LastUpdated = DateTime.UtcNow, Count = animeDtos.Count,
                Animes = animeDtos, RetryAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search anime for query: {Query}", query);
            return new AnimeListResponse
            {
                Success = false, DataSource = DataSource.Api, IsStale = true,
                Message = $"Search failed: {ex.Message}",
                LastUpdated = null, Count = 0,
                Animes = new List<AnimeInfoDto>(), RetryAttempts = 0
            };
        }
    }

    #region Enrichment Helper Methods

    /// <summary>
    /// Enrich anime entities that are missing landscape images
    /// </summary>
    private async Task EnrichMissingLandscapesAsync(List<AnimeInfoEntity> animes, CancellationToken cancellationToken)
    {
        foreach (var anime in animes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var searchTitle = !string.IsNullOrEmpty(anime.NameJapanese)
                    ? anime.NameJapanese
                    : anime.NameChinese;

                if (string.IsNullOrEmpty(searchTitle)) continue;

                // Try TMDB first
                var (_, _, landscapeUrl, _) = await EnrichWithTmdbAsync(searchTitle, anime.AirDate);

                // If TMDB failed, try AniList
                if (string.IsNullOrEmpty(landscapeUrl))
                {
                    var anilistData = await EnrichWithAniListAsync(searchTitle);
                    landscapeUrl = anilistData?.BannerImage ?? "";
                }

                if (!string.IsNullOrEmpty(landscapeUrl))
                {
                    anime.ImageLandscape = landscapeUrl;
                    _logger.LogDebug("Enriched landscape for '{Title}': {Url}", searchTitle, landscapeUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enrich landscape for anime {Id}", anime.BangumiId);
            }
        }
    }

    /// <summary>
    /// Enrich anime data with TMDB (English title, description, landscape image)
    /// </summary>
    private async Task<(string enTitle, string enDesc, string landscapeUrl, string tmdbUrl)> EnrichWithTmdbAsync(
        string jpTitle, string? airDate)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jpTitle)) return ("", "", "", "");

            var tmdbInfo = await _tmdbClient.GetAnimeSummaryAndBackdropAsync(jpTitle, airDate);
            if (tmdbInfo == null) return ("", "", "", "");

            return (
                tmdbInfo.EnglishTitle ?? "",
                tmdbInfo.ChineseSummary ?? tmdbInfo.EnglishSummary ?? "",
                tmdbInfo.BackdropUrl ?? "",
                !string.IsNullOrEmpty(tmdbInfo.TMDBID) ? $"https://www.themoviedb.org/tv/{tmdbInfo.TMDBID}" : ""
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB enrichment failed for: {Title}", jpTitle);
            return ("", "", "", "");
        }
    }

    /// <summary>
    /// Enrich anime data with AniList (banner image, English data)
    /// </summary>
    private async Task<AniListAnimeInfo?> EnrichWithAniListAsync(string jpTitle)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jpTitle)) return null;
            return await _aniListClient.GetAnimeInfoAsync(jpTitle);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AniList enrichment failed for: {Title}", jpTitle);
            return null;
        }
    }

    /// <summary>
    /// Enrich anime data with Bangumi (Chinese title, description, Bangumi ID)
    /// </summary>
    private async Task<(string bangumiId, string chTitle, string chDesc, string bangumiUrl)> EnrichWithBangumiAsync(
        string jpTitle, string enTitle)
    {
        try
        {
            // Try Japanese title first, then English title
            var searchTitle = !string.IsNullOrWhiteSpace(jpTitle) ? jpTitle : enTitle;
            if (string.IsNullOrWhiteSpace(searchTitle)) return ("", "", "", "");

            var result = await _bangumiClient.SearchByTitleAsync(searchTitle);
            if (result.ValueKind == JsonValueKind.Undefined) return ("", "", "", "");

            var id = result.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            var name = result.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var chTitle = result.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";
            chTitle = TitleLanguageResolver.ResolveFromName(name, chTitle: chTitle).chTitle;
            var chDesc = result.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? "" : "";

            // If no summary in search result, fetch detail
            if (string.IsNullOrEmpty(chDesc) && id > 0)
            {
                try
                {
                    var detail = await _bangumiClient.GetSubjectDetailAsync(id);
                    chDesc = detail.TryGetProperty("summary", out var detailSummary)
                        ? detailSummary.GetString() ?? "" : "";
                }
                catch { /* Ignore detail fetch errors */ }
            }

            return (
                id > 0 ? id.ToString() : "",
                chTitle,
                chDesc,
                id > 0 ? $"https://bgm.tv/subject/{id}" : ""
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bangumi enrichment failed for: {Title}", jpTitle);
            return ("", "", "", "");
        }
    }

    private async Task PersistTopAnimeCacheAsync(
        AnimeInfoEntity? existing,
        int bangumiId,
        string? jpTitle,
        string? chTitle,
        string? enTitle,
        string? chDesc,
        string? enDesc,
        string? score,
        string? portraitUrl,
        string? landscapeUrl,
        string? airDate,
        string? urlBangumi,
        string? urlTmdb,
        string? urlAnilist)
    {
        var candidate = new AnimeInfoEntity
        {
            BangumiId = bangumiId,
            NameJapanese = NullIfWhiteSpace(jpTitle),
            NameChinese = NullIfWhiteSpace(chTitle),
            NameEnglish = NullIfWhiteSpace(enTitle),
            DescChinese = NullIfWhiteSpace(chDesc),
            DescEnglish = NullIfWhiteSpace(enDesc),
            Score = NullIfWhiteSpace(score),
            ImagePortrait = NullIfWhiteSpace(portraitUrl),
            ImageLandscape = NullIfWhiteSpace(landscapeUrl),
            UrlBangumi = NullIfWhiteSpace(urlBangumi),
            UrlTmdb = NullIfWhiteSpace(urlTmdb),
            UrlAnilist = NullIfWhiteSpace(urlAnilist),
            AirDate = NullIfWhiteSpace(airDate),
            Weekday = existing?.Weekday ?? 0,
            IsPreFetched = existing?.IsPreFetched ?? false,
            MikanBangumiId = existing?.MikanBangumiId
        };

        await _repository.SaveAnimeInfoAsync(candidate);
    }

    private async Task<(List<AnimeInfoDto>? animes, DateTime? updatedAt)> TryReadTopListCacheAsync(
        string source,
        bool allowStale)
    {
        var cache = await _repository.GetTopAnimeCacheAsync(source);
        if (cache == null || string.IsNullOrWhiteSpace(cache.PayloadJson))
        {
            return (null, null);
        }

        var isStale = DateTime.UtcNow - cache.UpdatedAt > TopListCacheTtl;
        if (isStale && !allowStale)
        {
            return (null, cache.UpdatedAt);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<List<AnimeInfoDto>>(cache.PayloadJson);
            if (payload == null || payload.Count == 0)
            {
                return (null, cache.UpdatedAt);
            }

            return (payload, cache.UpdatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize top anime cache payload for source {Source}", source);
            return (null, cache.UpdatedAt);
        }
    }

    private async Task SaveTopListCacheAsync(string source, List<AnimeInfoDto> animes)
    {
        var payloadJson = JsonSerializer.Serialize(animes);
        await _repository.SaveTopAnimeCacheAsync(new TopAnimeCacheEntity
        {
            Source = source,
            PayloadJson = payloadJson
        });
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildMalFallbackId(JikanAnimeInfo anime)
    {
        if (anime.MalId > 0)
        {
            return $"mal:{anime.MalId}";
        }

        var seed = NormalizeTitleForComparison(anime.TitleJapanese)
            ?? NormalizeTitleForComparison(anime.TitleEnglish)
            ?? NormalizeTitleForComparison(anime.Title)
            ?? "unknown";

        return $"mal:{seed}";
    }

    private static bool IsCacheEntryCompatibleWithMalAnime(
        AnimeInfoEntity cacheEntry,
        string? malJapaneseTitle,
        string? malDefaultTitle)
    {
        var normalizedMalJp = NormalizeTitleForComparison(malJapaneseTitle);
        var normalizedCachedJp = NormalizeTitleForComparison(cacheEntry.NameJapanese);

        if (!string.IsNullOrWhiteSpace(normalizedMalJp) &&
            !string.IsNullOrWhiteSpace(normalizedCachedJp))
        {
            return string.Equals(
                normalizedMalJp,
                normalizedCachedJp,
                StringComparison.Ordinal);
        }

        var normalizedMalDefault = NormalizeTitleForComparison(malDefaultTitle);
        if (string.IsNullOrWhiteSpace(normalizedMalDefault))
        {
            return true;
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeTitleForComparison(cacheEntry.NameJapanese) ?? string.Empty,
            NormalizeTitleForComparison(cacheEntry.NameChinese) ?? string.Empty,
            NormalizeTitleForComparison(cacheEntry.NameEnglish) ?? string.Empty
        };

        candidates.Remove(string.Empty);
        return candidates.Contains(normalizedMalDefault);
    }

    private static string? NormalizeTitleForComparison(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.Normalize(NormalizationForm.FormKC).Trim();
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string ApplyKnownChineseTitleOverrides(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title ?? string.Empty;
        }

        var trimmed = title.Trim();
        if (trimmed.StartsWith("葬送的芙莉莲 ～", StringComparison.Ordinal) &&
            trimmed.EndsWith("的魔法～", StringComparison.Ordinal))
        {
            return "葬送的芙莉莲";
        }

        return trimmed;
    }

    #endregion

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Simple regex to strip HTML tags
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
    }
}
