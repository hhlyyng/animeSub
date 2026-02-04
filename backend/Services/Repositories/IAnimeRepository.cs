using backend.Data.Entities;

namespace backend.Services.Repositories;

/// <summary>
/// Repository interface for anime data persistence
/// </summary>
public interface IAnimeRepository
{
    // Daily schedule cache
    Task<List<int>?> GetDailyScheduleAsync(string date);
    Task SaveDailyScheduleAsync(string date, List<int> bangumiIds);
    Task<DateTime?> GetDailyScheduleCacheTimeAsync(string date);

    // Anime info
    Task<AnimeInfoEntity?> GetAnimeInfoAsync(int bangumiId);
    Task SaveAnimeInfoAsync(AnimeInfoEntity anime);
    Task SaveAnimeInfoBatchAsync(List<AnimeInfoEntity> animes);
    Task<List<AnimeInfoEntity>> GetAnimeInfoBatchAsync(List<int> bangumiIds);
    Task<List<AnimeInfoEntity>> GetAnimesByWeekdayAsync(int weekday);
    Task<List<AnimeInfoEntity>> GetPreFetchedAnimesAsync(List<int> bangumiIds);

    // Anime images
    Task<AnimeImagesEntity?> GetAnimeImagesAsync(int bangumiId);
    Task SaveAnimeImagesAsync(AnimeImagesEntity images);
    Task<List<AnimeImagesEntity>> GetAnimeImagesBatchAsync(List<int> bangumiIds);

    // Maintenance
    Task ClearAllAnimeDataAsync();
}
