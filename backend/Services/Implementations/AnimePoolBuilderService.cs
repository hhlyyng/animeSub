using System.Text.Json;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services.Interfaces;
using backend.Services.Repositories;

namespace backend.Services.Implementations;

/// <summary>
/// Background service that builds and refreshes the random anime recommendation pool (~250 items).
/// Runs once at startup (after 5s delay) then every 24 hours.
/// Sources: AniList TRENDING/POPULARITY/SCORE (50 each), Bangumi top (50), Jikan pages 1+2 (25 each)
/// </summary>
public class AnimePoolBuilderService : BackgroundService
{
    private const string DbSource = "random_pool";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan TmdbEnrichDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan JikanPageDelay = TimeSpan.FromMilliseconds(400);

    private readonly AnimePoolService _poolService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnimePoolBuilderService> _logger;

    public AnimePoolBuilderService(
        AnimePoolService poolService,
        IServiceScopeFactory scopeFactory,
        ILogger<AnimePoolBuilderService> logger)
    {
        _poolService = poolService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnimePoolBuilderService started");

        // Initial delay to let the app fully start up
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if pool needs building
                bool needsBuild = await ShouldRebuildAsync(stoppingToken);
                if (needsBuild)
                {
                    await BuildPoolAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AnimePoolBuilderService encountered an unexpected error");
            }

            // Wait until next 24h window
            await Task.Delay(RebuildInterval, stoppingToken);
        }
    }

    private async Task<bool> ShouldRebuildAsync(CancellationToken ct)
    {
        // If pool is already in memory, check age
        if (_poolService.PoolSize > 0)
            return false;

        // Check SQLite
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimeRepository>();
        var entity = await repository.GetTopAnimeCacheAsync(DbSource);

        if (entity == null)
            return true;

        bool isStale = DateTime.UtcNow - entity.UpdatedAt > RebuildInterval;

        if (!isStale && !string.IsNullOrWhiteSpace(entity.PayloadJson))
        {
            // Fresh pool exists in SQLite — warm the memory cache so PoolSize > 0
            // and any concurrent requests resolve immediately without hitting SQLite again.
            try
            {
                var pool = JsonSerializer.Deserialize<List<AnimeInfoDto>>(entity.PayloadJson);
                if (pool != null && pool.Count > 0)
                {
                    _poolService.UpdateMemoryCache(pool);
                    _logger.LogInformation(
                        "Warmed random pool from SQLite at startup ({Count} items, age {Age:F1}h)",
                        pool.Count,
                        (DateTime.UtcNow - entity.UpdatedAt).TotalHours);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm random pool from SQLite cache");
            }
        }

        return isStale;
    }

    private async Task BuildPoolAsync(CancellationToken ct)
    {
        _poolService.SetBuilding(true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Building random anime pool...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var anilistClient = scope.ServiceProvider.GetRequiredService<IAniListClient>();
            var bangumiClient = scope.ServiceProvider.GetRequiredService<IBangumiClient>();
            var jikanClient = scope.ServiceProvider.GetRequiredService<IJikanClient>();
            var tmdbClient = scope.ServiceProvider.GetRequiredService<ITMDBClient>();
            var repository = scope.ServiceProvider.GetRequiredService<IAnimeRepository>();
            var tokenStorage = scope.ServiceProvider.GetRequiredService<ITokenStorageService>();

            // Set TMDB token for enrichment (null-safe)
            var tmdbToken = await tokenStorage.GetTmdbTokenAsync();
            tmdbClient.SetToken(tmdbToken);

            // --- Step 1: Collect raw items from all sources ---
            var rawItems = new List<RawPoolItem>();
            var poolKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // AniList: 3 sort orders in parallel
            var anilistTasks = Task.WhenAll(
                anilistClient.GetTrendingAnimeAsync(50),
                anilistClient.GetAnimeByPopularityAsync(50),
                anilistClient.GetAnimeByScoreAsync(50));

            // Bangumi: top ranked
            var bangumiTask = FetchBangumiRawItemsAsync(bangumiClient, 50, ct);

            // Jikan: two pages sequentially (rate limit: 3 req/s)
            var jikanTask = FetchJikanRawItemsAsync(jikanClient, ct);

            await Task.WhenAll(anilistTasks, bangumiTask, jikanTask);

            // Merge AniList results
            var anilistResults = await anilistTasks;
            foreach (var batch in anilistResults)
            {
                foreach (var anime in batch)
                {
                    var key = $"al:{anime.AnilistId}";
                    if (!poolKeys.Add(key)) continue;

                    rawItems.Add(new RawPoolItem(
                        PoolKey: key,
                        SearchTitle: string.IsNullOrWhiteSpace(anime.NativeTitle) ? anime.EnglishTitle : anime.NativeTitle,
                        NativeTitle: anime.NativeTitle,
                        EnTitle: anime.EnglishTitle,
                        EnDesc: anime.EnglishSummary,
                        AirDate: null,
                        Portrait: anime.CoverUrl,
                        BannerImage: anime.BannerImage,
                        Score: anime.Score,
                        AnilistUrl: anime.OriSiteUrl,
                        BangumiUrl: null,
                        MalUrl: null));
                }
            }

            // Merge Bangumi results
            foreach (var item in await bangumiTask)
            {
                var key = $"bgm:{item.PoolKey}";
                if (!poolKeys.Add(key)) continue;
                rawItems.Add(item with { PoolKey = key });
            }

            // Merge Jikan results
            foreach (var item in await jikanTask)
            {
                var key = $"mal:{item.PoolKey}";
                if (!poolKeys.Add(key)) continue;
                rawItems.Add(item with { PoolKey = key });
            }

            _logger.LogInformation("Collected {Count} unique items for random pool enrichment", rawItems.Count);

            // --- Step 2: TMDB enrichment (sequential to stay within rate limits) ---
            var animeDtos = new List<AnimeInfoDto>();

            for (int i = 0; i < rawItems.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var item = rawItems[i];
                var dto = await EnrichWithTmdbAsync(tmdbClient, item);
                animeDtos.Add(dto);

                // Incremental save every 50 items
                if (animeDtos.Count % 50 == 0)
                {
                    await SavePartialPoolAsync(repository, animeDtos);
                    _poolService.UpdateMemoryCache(animeDtos.ToList());
                    _logger.LogInformation("Random pool partial save: {Count}/{Total}", animeDtos.Count, rawItems.Count);
                }

                // Small delay to stay within TMDB rate limits (~150ms → ~6.7 req/s, well within 40/10s)
                if (!string.IsNullOrWhiteSpace(item.SearchTitle))
                    await Task.Delay(TmdbEnrichDelay, ct);
            }

            // Final save
            await SavePartialPoolAsync(repository, animeDtos);
            _poolService.UpdateMemoryCache(animeDtos.ToList());

            stopwatch.Stop();
            _logger.LogInformation(
                "Random pool built: {Count} items in {Elapsed:F1}s",
                animeDtos.Count,
                stopwatch.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AnimePoolBuilderService build cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build random anime pool");
        }
        finally
        {
            _poolService.SetBuilding(false);
        }
    }

    private static async Task<List<RawPoolItem>> FetchBangumiRawItemsAsync(
        IBangumiClient bangumiClient,
        int limit,
        CancellationToken ct)
    {
        try
        {
            var topSubjects = await bangumiClient.SearchTopSubjectsAsync(limit);
            var items = new List<RawPoolItem>();

            foreach (var subject in topSubjects.EnumerateArray())
            {
                var id = subject.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                if (id == 0) continue;

                var jpTitle = subject.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var airDate = subject.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;

                string portraitUrl = "";
                if (subject.TryGetProperty("images", out var imagesEl) &&
                    imagesEl.TryGetProperty("large", out var largeEl))
                {
                    portraitUrl = largeEl.GetString() ?? "";
                }

                items.Add(new RawPoolItem(
                    PoolKey: id.ToString(),
                    SearchTitle: jpTitle,
                    NativeTitle: jpTitle,
                    EnTitle: null,
                    EnDesc: null,
                    AirDate: airDate,
                    Portrait: portraitUrl,
                    BannerImage: "",
                    Score: null,
                    BangumiUrl: $"https://bgm.tv/subject/{id}",
                    AnilistUrl: null,
                    MalUrl: null));
            }

            return items;
        }
        catch (Exception)
        {
            return new List<RawPoolItem>();
        }
    }

    private static async Task<List<RawPoolItem>> FetchJikanRawItemsAsync(
        IJikanClient jikanClient,
        CancellationToken ct)
    {
        var items = new List<RawPoolItem>();

        try
        {
            var page1 = await jikanClient.GetTopAnimePageAsync(1, 25);
            AddJikanItems(items, page1);

            await Task.Delay(JikanPageDelay, ct);

            var page2 = await jikanClient.GetTopAnimePageAsync(2, 25);
            AddJikanItems(items, page2);
        }
        catch (Exception)
        {
            // Partial results are fine
        }

        return items;
    }

    private static void AddJikanItems(List<RawPoolItem> items, List<backend.Models.Jikan.JikanAnimeInfo> source)
    {
        foreach (var anime in source)
        {
            var portraitUrl = anime.Images?.Jpg?.LargeImageUrl
                ?? anime.Images?.Jpg?.ImageUrl
                ?? anime.Images?.Webp?.LargeImageUrl
                ?? "";

            items.Add(new RawPoolItem(
                PoolKey: anime.MalId.ToString(),
                SearchTitle: anime.TitleJapanese ?? anime.Title,
                NativeTitle: anime.TitleJapanese,
                EnTitle: anime.TitleEnglish ?? anime.Title,
                EnDesc: anime.Synopsis,
                AirDate: null,
                Portrait: portraitUrl,
                BannerImage: "",
                Score: anime.Score?.ToString("F1"),
                BangumiUrl: null,
                AnilistUrl: null,
                MalUrl: anime.Url));
        }
    }

    private static async Task<AnimeInfoDto> EnrichWithTmdbAsync(
        ITMDBClient tmdbClient,
        RawPoolItem item)
    {
        string landscapeUrl = "";
        string chTitle = "";
        string chDesc = "";
        string enTitle = item.EnTitle ?? "";
        string enDesc = item.EnDesc ?? "";
        string tmdbUrl = "";

        if (!string.IsNullOrWhiteSpace(item.SearchTitle))
        {
            try
            {
                var tmdbInfo = await tmdbClient.GetAnimeSummaryAndBackdropAsync(item.SearchTitle, item.AirDate);
                if (tmdbInfo != null)
                {
                    landscapeUrl = tmdbInfo.BackdropUrl ?? "";
                    chTitle = tmdbInfo.ChineseTitle ?? "";
                    chDesc = tmdbInfo.ChineseSummary ?? "";
                    if (string.IsNullOrWhiteSpace(enTitle))
                        enTitle = tmdbInfo.EnglishTitle ?? "";
                    if (string.IsNullOrWhiteSpace(enDesc))
                        enDesc = tmdbInfo.EnglishSummary ?? "";
                    tmdbUrl = tmdbInfo.OriSiteUrl ?? "";
                }
            }
            catch
            {
                // Continue without TMDB enrichment
            }
        }

        // Fallback landscape: AniList banner image
        if (string.IsNullOrWhiteSpace(landscapeUrl) && !string.IsNullOrWhiteSpace(item.BannerImage))
            landscapeUrl = item.BannerImage;

        return new AnimeInfoDto
        {
            BangumiId = "",
            JpTitle = item.NativeTitle ?? "",
            ChTitle = chTitle,
            EnTitle = enTitle,
            ChDesc = string.IsNullOrEmpty(chDesc) ? "无可用中文介绍" : chDesc,
            EnDesc = string.IsNullOrEmpty(enDesc) ? "No English description available" : enDesc,
            Score = item.Score ?? "0",
            Images = new AnimeImagesDto
            {
                Portrait = item.Portrait ?? "",
                Landscape = landscapeUrl
            },
            ExternalUrls = new ExternalUrlsDto
            {
                Bangumi = item.BangumiUrl ?? "",
                Tmdb = tmdbUrl,
                Anilist = item.AnilistUrl ?? ""
            }
        };
    }

    private async Task SavePartialPoolAsync(IAnimeRepository repository, List<AnimeInfoDto> animeDtos)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(animeDtos);
            await repository.SaveTopAnimeCacheAsync(new TopAnimeCacheEntity
            {
                Source = DbSource,
                PayloadJson = payloadJson
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save partial random pool to SQLite");
        }
    }

    private record RawPoolItem(
        string PoolKey,
        string? SearchTitle,
        string? NativeTitle,
        string? EnTitle,
        string? EnDesc,
        string? AirDate,
        string Portrait,
        string BannerImage,
        string? Score,
        string? BangumiUrl,
        string? AnilistUrl,
        string? MalUrl);
}
