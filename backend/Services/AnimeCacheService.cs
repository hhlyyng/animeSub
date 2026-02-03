using Microsoft.Extensions.Caching.Memory;
using backend.Services.Repositories;
using backend.Data.Entities;

namespace backend.Services;

/// <summary>
/// Interface for anime caching service
/// Coordinates between IMemoryCache (fast, volatile) and SQLite (persistent)
/// </summary>
public interface IAnimeCacheService
{
    // Daily schedule caching
    Task<List<int>?> GetTodayScheduleCachedAsync();
    Task CacheTodayScheduleAsync(List<int> bangumiIds);
    Task<DateTime?> GetTodayScheduleCacheTimeAsync();

    // Full anime data caching (for fallback)
    Task<List<object>?> GetCachedAnimeListAsync();
    Task CacheAnimeListAsync(List<object> animes);

    // Anime images caching
    Task<AnimeImagesEntity?> GetAnimeImagesCachedAsync(int bangumiId);
    Task CacheAnimeImagesAsync(int bangumiId, string? posterUrl, string? backdropUrl, int? tmdbId);
    Task<Dictionary<int, AnimeImagesEntity>> GetAnimeImagesBatchCachedAsync(List<int> bangumiIds);
}

/// <summary>
/// Two-tier caching service: IMemoryCache (L1) + SQLite (L2)
/// Flow: Memory → SQLite → External API
/// </summary>
public class AnimeCacheService : IAnimeCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IAnimeRepository _repository;
    private readonly ILogger<AnimeCacheService> _logger;

    public AnimeCacheService(
        IMemoryCache memoryCache,
        IAnimeRepository repository,
        ILogger<AnimeCacheService> logger)
    {
        _memoryCache = memoryCache;
        _repository = repository;
        _logger = logger;
    }

    #region Daily Schedule

    public async Task<List<int>?> GetTodayScheduleCachedAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var cacheKey = $"daily_schedule_{today}";

        // Level 1: Check memory cache
        if (_memoryCache.TryGetValue(cacheKey, out List<int>? cached))
        {
            _logger.LogInformation("Daily schedule retrieved from memory cache ({Count} anime)", cached?.Count ?? 0);
            return cached;
        }

        // Level 2: Check SQLite
        var dbCached = await _repository.GetDailyScheduleAsync(today);
        if (dbCached != null && dbCached.Count > 0)
        {
            _logger.LogInformation("Daily schedule retrieved from SQLite cache ({Count} anime)", dbCached.Count);

            // Populate memory cache (expire at tomorrow 00:00)
            var tomorrow = DateTime.Today.AddDays(1);
            _memoryCache.Set(cacheKey, dbCached, tomorrow);

            return dbCached;
        }

        _logger.LogInformation("No cached daily schedule found for {Date}", today);
        return null;
    }

    public async Task CacheTodayScheduleAsync(List<int> bangumiIds)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var cacheKey = $"daily_schedule_{today}";

        // Save to SQLite (persistent)
        await _repository.SaveDailyScheduleAsync(today, bangumiIds);

        // Save to memory cache (expire at tomorrow 00:00)
        var tomorrow = DateTime.Today.AddDays(1);
        _memoryCache.Set(cacheKey, bangumiIds, tomorrow);

        _logger.LogInformation("Daily schedule cached for {Date} ({Count} anime)", today, bangumiIds.Count);
    }

    public async Task<DateTime?> GetTodayScheduleCacheTimeAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return await _repository.GetDailyScheduleCacheTimeAsync(today);
    }

    #endregion

    #region Full Anime List Cache (for fallback)

    private const string ANIME_LIST_CACHE_KEY = "anime_list_today";

    public Task<List<object>?> GetCachedAnimeListAsync()
    {
        if (_memoryCache.TryGetValue(ANIME_LIST_CACHE_KEY, out List<object>? cached))
        {
            _logger.LogInformation("Full anime list retrieved from memory cache ({Count} items)", cached?.Count ?? 0);
            return Task.FromResult(cached);
        }

        _logger.LogDebug("No cached anime list found in memory");
        return Task.FromResult<List<object>?>(null);
    }

    public Task CacheAnimeListAsync(List<object> animes)
    {
        // Cache in memory until tomorrow 00:00
        var tomorrow = DateTime.Today.AddDays(1);
        _memoryCache.Set(ANIME_LIST_CACHE_KEY, animes, tomorrow);

        _logger.LogInformation("Full anime list cached in memory ({Count} items)", animes.Count);
        return Task.CompletedTask;
    }

    #endregion

    #region Anime Images

    public async Task<AnimeImagesEntity?> GetAnimeImagesCachedAsync(int bangumiId)
    {
        var cacheKey = $"anime_images_{bangumiId}";

        // Level 1: Check memory cache
        if (_memoryCache.TryGetValue(cacheKey, out AnimeImagesEntity? cached))
        {
            _logger.LogDebug("Anime images retrieved from memory cache for BangumiId {BangumiId}", bangumiId);
            return cached;
        }

        // Level 2: Check SQLite
        var dbCached = await _repository.GetAnimeImagesAsync(bangumiId);
        if (dbCached != null)
        {
            _logger.LogInformation("Anime images retrieved from SQLite for BangumiId {BangumiId}", bangumiId);

            // Populate memory cache (30 days expiration)
            _memoryCache.Set(cacheKey, dbCached, TimeSpan.FromDays(30));

            return dbCached;
        }

        _logger.LogDebug("No cached images found for BangumiId {BangumiId}", bangumiId);
        return null;
    }

    public async Task<Dictionary<int, AnimeImagesEntity>> GetAnimeImagesBatchCachedAsync(List<int> bangumiIds)
    {
        var result = new Dictionary<int, AnimeImagesEntity>();
        var uncachedIds = new List<int>();

        // Check memory cache for each ID
        foreach (var id in bangumiIds)
        {
            var cacheKey = $"anime_images_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out AnimeImagesEntity? cached))
            {
                result[id] = cached!;
            }
            else
            {
                uncachedIds.Add(id);
            }
        }

        if (uncachedIds.Count == 0)
        {
            _logger.LogInformation("All {Count} anime images retrieved from memory cache", bangumiIds.Count);
            return result;
        }

        // Check SQLite for uncached IDs
        var dbCached = await _repository.GetAnimeImagesBatchAsync(uncachedIds);
        foreach (var images in dbCached)
        {
            result[images.BangumiId] = images;

            // Populate memory cache
            var cacheKey = $"anime_images_{images.BangumiId}";
            _memoryCache.Set(cacheKey, images, TimeSpan.FromDays(30));
        }

        _logger.LogInformation("Batch anime images query: {Cached}/{Total} from cache",
            result.Count, bangumiIds.Count);

        return result;
    }

    public async Task CacheAnimeImagesAsync(int bangumiId, string? posterUrl, string? backdropUrl, int? tmdbId)
    {
        var cacheKey = $"anime_images_{bangumiId}";

        var images = new AnimeImagesEntity
        {
            BangumiId = bangumiId,
            PosterUrl = posterUrl,
            BackdropUrl = backdropUrl,
            TmdbId = tmdbId
        };

        // Save to SQLite (persistent)
        await _repository.SaveAnimeImagesAsync(images);

        // Save to memory cache (30 days expiration)
        _memoryCache.Set(cacheKey, images, TimeSpan.FromDays(30));

        _logger.LogInformation("Anime images cached for BangumiId {BangumiId}", bangumiId);
    }

    #endregion
}
