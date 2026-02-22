using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services.Interfaces;
using backend.Services.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace backend.Services.Implementations;

/// <summary>
/// Singleton service that provides random anime picks from the pre-built pool.
/// Pool is stored in SQLite (random_pool) and cached in IMemoryCache.
/// </summary>
public class AnimePoolService : IAnimePoolService
{
    private const string MemoryCacheKey = "random_pool_list";
    private const string DbSource = "random_pool";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IMemoryCache _memoryCache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnimePoolService> _logger;

    private volatile bool _isBuilding;

    public bool IsBuilding => _isBuilding;

    public int PoolSize
    {
        get
        {
            if (_memoryCache.TryGetValue(MemoryCacheKey, out List<AnimeInfoDto>? cached) && cached != null)
                return cached.Count;
            return 0;
        }
    }

    public AnimePoolService(
        IMemoryCache memoryCache,
        IServiceScopeFactory scopeFactory,
        ILogger<AnimePoolService> logger)
    {
        _memoryCache = memoryCache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<AnimeInfoDto>> GetRandomPicksAsync(int count, CancellationToken ct = default)
    {
        // L1: memory cache
        if (_memoryCache.TryGetValue(MemoryCacheKey, out List<AnimeInfoDto>? cachedList) && cachedList != null && cachedList.Count > 0)
        {
            return PickRandom(cachedList, count);
        }

        // L2: SQLite
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimeRepository>();
        var entity = await repository.GetTopAnimeCacheAsync(DbSource);

        if (entity == null || string.IsNullOrWhiteSpace(entity.PayloadJson))
            return new List<AnimeInfoDto>();

        try
        {
            var pool = JsonSerializer.Deserialize<List<AnimeInfoDto>>(entity.PayloadJson);
            if (pool == null || pool.Count == 0)
                return new List<AnimeInfoDto>();

            // Warm memory cache
            _memoryCache.Set(MemoryCacheKey, pool, CacheTtl);
            _logger.LogInformation("Loaded random pool from SQLite into memory cache ({Count} items)", pool.Count);
            return PickRandom(pool, count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize random pool from SQLite");
            return new List<AnimeInfoDto>();
        }
    }

    /// <summary>
    /// Update the in-memory pool cache. Called by the builder after each partial/final save.
    /// </summary>
    public void UpdateMemoryCache(List<AnimeInfoDto> pool)
    {
        _memoryCache.Set(MemoryCacheKey, pool, CacheTtl);
    }

    /// <summary>
    /// Set or clear the building flag. Called by AnimePoolBuilderService.
    /// </summary>
    public void SetBuilding(bool building) => _isBuilding = building;

    private static List<AnimeInfoDto> PickRandom(List<AnimeInfoDto> pool, int count)
    {
        var copy = pool.ToArray();
        Random.Shared.Shuffle(copy.AsSpan());
        return copy.Take(count).ToList();
    }
}
