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
    /// Create a new subscription
    /// </summary>
    Task<SubscriptionResponse> CreateSubscriptionAsync(CreateSubscriptionRequest request);

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
    /// Filter RSS items based on subscription keywords
    /// </summary>
    List<MikanRssItem> FilterRssItems(List<MikanRssItem> items, string? keywordInclude, string? keywordExclude);
}
