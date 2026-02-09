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
            .Where(a => bangumiIds.Contains(a.BangumiId))
            .ToListAsync();

        _logger.LogInformation("Batch anime info query: {Found}/{Total} found in cache",
            animes.Count, bangumiIds.Count);

        return animes;
    }

    public async Task<AnimeInfoEntity?> FindAnimeInfoByAnyTitleAsync(params string?[] titles)
    {
        var normalized = titles
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToLower())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            return null;
        }

        var result = await _context.AnimeInfos
            .Where(a =>
                (a.NameJapanese != null && normalized.Contains(a.NameJapanese.Trim().ToLower())) ||
                (a.NameChinese != null && normalized.Contains(a.NameChinese.Trim().ToLower())) ||
                (a.NameEnglish != null && normalized.Contains(a.NameEnglish.Trim().ToLower())))
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync();

        if (result != null)
        {
            _logger.LogDebug(
                "Anime info title cache hit for BangumiId {BangumiId} via titles [{Titles}]",
                result.BangumiId,
                string.Join(", ", normalized));
        }

        return result;
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
            // Update all fields
            existing.NameChinese = anime.NameChinese ?? existing.NameChinese;
            existing.NameJapanese = anime.NameJapanese ?? existing.NameJapanese;
            existing.NameEnglish = anime.NameEnglish ?? existing.NameEnglish;
            existing.DescChinese = anime.DescChinese ?? existing.DescChinese;
            existing.DescEnglish = anime.DescEnglish ?? existing.DescEnglish;
            existing.Score = anime.Score ?? existing.Score;
            existing.ImagePortrait = anime.ImagePortrait ?? existing.ImagePortrait;
            existing.ImageLandscape = anime.ImageLandscape ?? existing.ImageLandscape;
            existing.TmdbId = anime.TmdbId ?? existing.TmdbId;
            existing.AnilistId = anime.AnilistId ?? existing.AnilistId;
            existing.UrlBangumi = anime.UrlBangumi ?? existing.UrlBangumi;
            existing.UrlTmdb = anime.UrlTmdb ?? existing.UrlTmdb;
            existing.UrlAnilist = anime.UrlAnilist ?? existing.UrlAnilist;
            existing.AirDate = anime.AirDate ?? existing.AirDate;
            existing.Weekday = anime.Weekday != 0 ? anime.Weekday : existing.Weekday;
            existing.IsPreFetched = anime.IsPreFetched || existing.IsPreFetched;
            existing.UpdatedAt = DateTime.UtcNow;
            _logger.LogDebug("Updated anime info for BangumiId {BangumiId}", anime.BangumiId);
        }

        await _context.SaveChangesAsync();
    }

    public async Task SaveAnimeInfoBatchAsync(List<AnimeInfoEntity> animes)
    {
        foreach (var anime in animes)
        {
            var existing = await _context.AnimeInfos
                .FirstOrDefaultAsync(a => a.BangumiId == anime.BangumiId);

            if (existing == null)
            {
                anime.CreatedAt = DateTime.UtcNow;
                anime.UpdatedAt = DateTime.UtcNow;
                _context.AnimeInfos.Add(anime);
            }
            else
            {
                // Update all fields
                existing.NameChinese = anime.NameChinese ?? existing.NameChinese;
                existing.NameJapanese = anime.NameJapanese ?? existing.NameJapanese;
                existing.NameEnglish = anime.NameEnglish ?? existing.NameEnglish;
                existing.DescChinese = anime.DescChinese ?? existing.DescChinese;
                existing.DescEnglish = anime.DescEnglish ?? existing.DescEnglish;
                existing.Score = anime.Score ?? existing.Score;
                existing.ImagePortrait = anime.ImagePortrait ?? existing.ImagePortrait;
                existing.ImageLandscape = anime.ImageLandscape ?? existing.ImageLandscape;
                existing.TmdbId = anime.TmdbId ?? existing.TmdbId;
                existing.AnilistId = anime.AnilistId ?? existing.AnilistId;
                existing.UrlBangumi = anime.UrlBangumi ?? existing.UrlBangumi;
                existing.UrlTmdb = anime.UrlTmdb ?? existing.UrlTmdb;
                existing.UrlAnilist = anime.UrlAnilist ?? existing.UrlAnilist;
                existing.AirDate = anime.AirDate ?? existing.AirDate;
                existing.Weekday = anime.Weekday != 0 ? anime.Weekday : existing.Weekday;
                existing.IsPreFetched = anime.IsPreFetched || existing.IsPreFetched;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Batch saved {Count} anime info records", animes.Count);
    }

    public async Task<List<AnimeInfoEntity>> GetAnimesByWeekdayAsync(int weekday)
    {
        var animes = await _context.AnimeInfos
            .Where(a => a.Weekday == weekday && a.IsPreFetched)
            .ToListAsync();

        _logger.LogInformation("Found {Count} pre-fetched anime for weekday {Weekday}", animes.Count, weekday);
        return animes;
    }

    public async Task<List<AnimeInfoEntity>> GetPreFetchedAnimesAsync(List<int> bangumiIds)
    {
        var animes = await _context.AnimeInfos
            .Where(a => bangumiIds.Contains(a.BangumiId) && a.IsPreFetched)
            .ToListAsync();

        return animes;
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

    #region Maintenance

    public async Task ClearAllAnimeDataAsync()
    {
        // Clear anime info table
        var animeInfoCount = await _context.AnimeInfos.CountAsync();
        _context.AnimeInfos.RemoveRange(_context.AnimeInfos);

        // Clear anime images table
        var animeImagesCount = await _context.AnimeImages.CountAsync();
        _context.AnimeImages.RemoveRange(_context.AnimeImages);

        // Clear daily schedule cache
        var scheduleCacheCount = await _context.DailyScheduleCaches.CountAsync();
        _context.DailyScheduleCaches.RemoveRange(_context.DailyScheduleCaches);

        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "Cleared all anime data: {AnimeInfo} anime info, {AnimeImages} anime images, {ScheduleCache} schedule caches",
            animeInfoCount, animeImagesCount, scheduleCacheCount);
    }

    #endregion
}
