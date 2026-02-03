using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Data.Entities;
using System.Text.Json;

namespace backend.Services.Repositories;

/// <summary>
/// SQLite repository for anime data persistence
/// </summary>
public class AnimeRepository : IAnimeRepository
{
    private readonly AnimeDbContext _context;
    private readonly ILogger<AnimeRepository> _logger;

    public AnimeRepository(
        AnimeDbContext context,
        ILogger<AnimeRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Daily Schedule Cache

    public async Task<List<int>?> GetDailyScheduleAsync(string date)
    {
        var cache = await _context.DailyScheduleCaches
            .FirstOrDefaultAsync(c => c.Date == date);

        if (cache == null)
        {
            _logger.LogDebug("No daily schedule cache found for {Date}", date);
            return null;
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<int>>(cache.BangumiIdsJson);
            _logger.LogInformation("Daily schedule cache hit for {Date}: {Count} anime", date, ids?.Count ?? 0);
            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize daily schedule cache for {Date}", date);
            return null;
        }
    }

    public async Task SaveDailyScheduleAsync(string date, List<int> bangumiIds)
    {
        var cache = await _context.DailyScheduleCaches
            .FirstOrDefaultAsync(c => c.Date == date);

        var json = JsonSerializer.Serialize(bangumiIds);

        if (cache == null)
        {
            cache = new DailyScheduleCacheEntity
            {
                Date = date,
                BangumiIdsJson = json,
                CreatedAt = DateTime.UtcNow
            };
            _context.DailyScheduleCaches.Add(cache);
            _logger.LogInformation("Created daily schedule cache for {Date}", date);
        }
        else
        {
            cache.BangumiIdsJson = json;
            _logger.LogInformation("Updated daily schedule cache for {Date}", date);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<DateTime?> GetDailyScheduleCacheTimeAsync(string date)
    {
        var cache = await _context.DailyScheduleCaches
            .FirstOrDefaultAsync(c => c.Date == date);

        return cache?.CreatedAt;
    }

    #endregion

    #region Anime Info

    public async Task<AnimeInfoEntity?> GetAnimeInfoAsync(int bangumiId)
    {
        var anime = await _context.AnimeInfos
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.BangumiId == bangumiId);

        if (anime != null)
        {
            _logger.LogDebug("Anime info cache hit for BangumiId {BangumiId}", bangumiId);
        }

        return anime;
    }

    public async Task<List<AnimeInfoEntity>> GetAnimeInfoBatchAsync(List<int> bangumiIds)
    {
        var animes = await _context.AnimeInfos
            .Include(a => a.Images)
            .Where(a => bangumiIds.Contains(a.BangumiId))
            .ToListAsync();

        _logger.LogInformation("Batch anime info query: {Found}/{Total} found in cache",
            animes.Count, bangumiIds.Count);

        return animes;
    }

    public async Task SaveAnimeInfoAsync(AnimeInfoEntity anime)
    {
        var existing = await _context.AnimeInfos
            .FirstOrDefaultAsync(a => a.BangumiId == anime.BangumiId);

        if (existing == null)
        {
            anime.CreatedAt = DateTime.UtcNow;
            anime.UpdatedAt = DateTime.UtcNow;
            _context.AnimeInfos.Add(anime);
            _logger.LogInformation("Created anime info for BangumiId {BangumiId}", anime.BangumiId);
        }
        else
        {
            // Update fields
            existing.NameChinese = anime.NameChinese ?? existing.NameChinese;
            existing.NameJapanese = anime.NameJapanese ?? existing.NameJapanese;
            existing.NameEnglish = anime.NameEnglish ?? existing.NameEnglish;
            existing.Rating = anime.Rating ?? existing.Rating;
            existing.Summary = anime.Summary ?? existing.Summary;
            existing.AirDate = anime.AirDate ?? existing.AirDate;
            existing.Weekday = anime.Weekday;
            existing.UpdatedAt = DateTime.UtcNow;
            _logger.LogDebug("Updated anime info for BangumiId {BangumiId}", anime.BangumiId);
        }

        await _context.SaveChangesAsync();
    }

    #endregion

    #region Anime Images

    public async Task<AnimeImagesEntity?> GetAnimeImagesAsync(int bangumiId)
    {
        var images = await _context.AnimeImages
            .FirstOrDefaultAsync(i => i.BangumiId == bangumiId);

        if (images != null)
        {
            _logger.LogDebug("Anime images cache hit for BangumiId {BangumiId}", bangumiId);
        }

        return images;
    }

    public async Task<List<AnimeImagesEntity>> GetAnimeImagesBatchAsync(List<int> bangumiIds)
    {
        var images = await _context.AnimeImages
            .Where(i => bangumiIds.Contains(i.BangumiId))
            .ToListAsync();

        _logger.LogInformation("Batch anime images query: {Found}/{Total} found in cache",
            images.Count, bangumiIds.Count);

        return images;
    }

    public async Task SaveAnimeImagesAsync(AnimeImagesEntity images)
    {
        var existing = await _context.AnimeImages
            .FirstOrDefaultAsync(i => i.BangumiId == images.BangumiId);

        if (existing == null)
        {
            images.CreatedAt = DateTime.UtcNow;
            images.UpdatedAt = DateTime.UtcNow;
            _context.AnimeImages.Add(images);
            _logger.LogInformation("Created anime images for BangumiId {BangumiId}", images.BangumiId);
        }
        else
        {
            // Update fields
            existing.PosterUrl = images.PosterUrl ?? existing.PosterUrl;
            existing.BackdropUrl = images.BackdropUrl ?? existing.BackdropUrl;
            existing.TmdbId = images.TmdbId ?? existing.TmdbId;
            existing.AniListId = images.AniListId ?? existing.AniListId;
            existing.UpdatedAt = DateTime.UtcNow;
            _logger.LogDebug("Updated anime images for BangumiId {BangumiId}", images.BangumiId);
        }

        await _context.SaveChangesAsync();
    }

    #endregion
}
