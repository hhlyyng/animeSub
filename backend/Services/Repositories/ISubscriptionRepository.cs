using backend.Data.Entities;

namespace backend.Services.Repositories;

/// <summary>
/// Repository interface for subscription data persistence
/// </summary>
public interface ISubscriptionRepository
{
    // Subscription CRUD
    Task<List<SubscriptionEntity>> GetAllSubscriptionsAsync();
    Task<List<SubscriptionEntity>> GetEnabledSubscriptionsAsync();
    Task<SubscriptionEntity?> GetSubscriptionByIdAsync(int id);
    Task<SubscriptionEntity?> GetSubscriptionByBangumiIdAsync(int bangumiId);
    Task<SubscriptionEntity> CreateSubscriptionAsync(SubscriptionEntity subscription);
    Task<SubscriptionEntity> UpdateSubscriptionAsync(SubscriptionEntity subscription);
    Task DeleteSubscriptionAsync(int id);

    // Download history
    Task<List<DownloadHistoryEntity>> GetDownloadHistoryAsync(int subscriptionId, int limit = 50);
    Task<List<DownloadHistoryEntity>> GetAllDownloadHistoryBySubscriptionIdAsync(int subscriptionId);
    Task<List<DownloadHistoryEntity>> GetManualDownloadHistoryByBangumiIdAsync(int bangumiId, int limit = 50);
    Task<List<DownloadHistoryEntity>> GetManualDownloadsWithAnimeContextAsync();
    Task<DownloadHistoryEntity?> GetDownloadByHashAsync(string torrentHash);
    Task<bool> ExistsDownloadByHashAsync(string torrentHash);
    Task<DownloadHistoryEntity> CreateDownloadHistoryAsync(DownloadHistoryEntity history);
    Task<DownloadHistoryEntity> UpdateDownloadHistoryAsync(DownloadHistoryEntity history);
    Task<List<DownloadHistoryEntity>> GetPendingDownloadsAsync();

    // Statistics
    Task UpdateSubscriptionLastCheckedAsync(int subscriptionId);
    Task UpdateSubscriptionLastDownloadAsync(int subscriptionId);
    Task IncrementDownloadCountAsync(int subscriptionId);
}
