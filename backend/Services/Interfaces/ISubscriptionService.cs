using backend.Models.Dtos;
using backend.Models.Mikan;

namespace backend.Services.Interfaces;

/// <summary>
/// Service interface for managing anime subscriptions
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Get all subscriptions
    /// </summary>
    Task<List<SubscriptionResponse>> GetAllSubscriptionsAsync();

    /// <summary>
    /// Get a subscription by ID
    /// </summary>
    Task<SubscriptionResponse?> GetSubscriptionByIdAsync(int id);

    /// <summary>
    /// Get a subscription by Bangumi ID
    /// </summary>
    Task<SubscriptionResponse?> GetSubscriptionByBangumiIdAsync(int bangumiId);

    /// <summary>
    /// Create a new subscription
    /// </summary>
    Task<SubscriptionResponse> CreateSubscriptionAsync(CreateSubscriptionRequest request);

    /// <summary>
    /// Ensure a subscription exists and is enabled for the target anime
    /// </summary>
    Task<SubscriptionResponse> EnsureSubscriptionAsync(CreateSubscriptionRequest request);

    /// <summary>
    /// Cancel subscription and process existing torrent tasks
    /// </summary>
    Task<CancelSubscriptionResponse?> CancelSubscriptionAsync(int subscriptionId, bool deleteFiles);

    /// <summary>
    /// Update a subscription
    /// </summary>
    Task<SubscriptionResponse?> UpdateSubscriptionAsync(int id, UpdateSubscriptionRequest request);

    /// <summary>
    /// Delete a subscription
    /// </summary>
    Task<bool> DeleteSubscriptionAsync(int id);

    /// <summary>
    /// Toggle subscription enabled status
    /// </summary>
    Task<SubscriptionResponse?> ToggleSubscriptionAsync(int id, bool enabled);

    /// <summary>
    /// Check a single subscription for new torrents
    /// </summary>
    Task<CheckSubscriptionResponse> CheckSubscriptionAsync(int id);

    /// <summary>
    /// Check all enabled subscriptions for new torrents
    /// </summary>
    Task<CheckAllSubscriptionsResponse> CheckAllSubscriptionsAsync();

    /// <summary>
    /// Get download history for a subscription
    /// </summary>
    Task<List<DownloadHistoryResponse>> GetDownloadHistoryAsync(int subscriptionId, int limit = 50);

    /// <summary>
    /// Get anime list that has manual download tasks but no active subscription record
    /// </summary>
    Task<List<ManualDownloadAnimeResponse>> GetManualOnlyDownloadAnimesAsync(int limit = 200);

    /// <summary>
    /// Get manual download history by anime Bangumi ID
    /// </summary>
    Task<List<DownloadHistoryResponse>> GetManualDownloadHistoryAsync(int bangumiId, int limit = 50);

    /// <summary>
    /// Get lightweight task hashes for a subscription (for qBittorrent correlation)
    /// </summary>
    Task<List<TaskHashResponse>> GetTaskHashesAsync(int subscriptionId, int limit = 300);

    /// <summary>
    /// Get lightweight task hashes for manual downloads by Bangumi ID
    /// </summary>
    Task<List<TaskHashResponse>> GetManualTaskHashesAsync(int bangumiId, int limit = 300);

    /// <summary>
    /// Filter RSS items based on subscription keywords
    /// </summary>
    List<MikanRssItem> FilterRssItems(List<MikanRssItem> items, string? keywordInclude, string? keywordExclude);
}
