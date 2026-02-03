using Microsoft.Extensions.Options;
using backend.Data.Entities;
using backend.Models.Configuration;
using backend.Models.Dtos;
using backend.Models.Mikan;
using backend.Services.Interfaces;
using backend.Services.Repositories;

namespace backend.Services.Implementations;

/// <summary>
/// Service implementation for managing anime subscriptions
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _repository;
    private readonly IMikanClient _mikanClient;
    private readonly IQBittorrentService _qbService;
    private readonly MikanConfiguration _mikanConfig;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionRepository repository,
        IMikanClient mikanClient,
        IQBittorrentService qbService,
        IOptions<MikanConfiguration> mikanConfig,
        ILogger<SubscriptionService> logger)
    {
        _repository = repository;
        _mikanClient = mikanClient;
        _qbService = qbService;
        _mikanConfig = mikanConfig.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionResponse>> GetAllSubscriptionsAsync()
    {
        var subscriptions = await _repository.GetAllSubscriptionsAsync();
        return subscriptions.Select(s => SubscriptionResponse.FromEntity(s, _mikanConfig.BaseUrl)).ToList();
    }

    public async Task<SubscriptionResponse?> GetSubscriptionByIdAsync(int id)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(id);
        return subscription == null ? null : SubscriptionResponse.FromEntity(subscription, _mikanConfig.BaseUrl);
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(CreateSubscriptionRequest request)
    {
        // Check if subscription already exists for this Bangumi ID
        var existing = await _repository.GetSubscriptionByBangumiIdAsync(request.BangumiId);
        if (existing != null)
        {
            _logger.LogWarning("Subscription already exists for BangumiId {BangumiId}", request.BangumiId);
            throw new InvalidOperationException($"Subscription already exists for BangumiId {request.BangumiId}");
        }

        var entity = new SubscriptionEntity
        {
            BangumiId = request.BangumiId,
            Title = request.Title,
            MikanBangumiId = request.MikanBangumiId,
            SubgroupId = request.SubgroupId,
            SubgroupName = request.SubgroupName,
            KeywordInclude = request.KeywordInclude,
            KeywordExclude = request.KeywordExclude,
            IsEnabled = true
        };

        var created = await _repository.CreateSubscriptionAsync(entity);
        _logger.LogInformation("Created subscription {Id} for {Title}", created.Id, created.Title);

        return SubscriptionResponse.FromEntity(created, _mikanConfig.BaseUrl);
    }

    public async Task<SubscriptionResponse?> UpdateSubscriptionAsync(int id, UpdateSubscriptionRequest request)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return null;
        }

        if (request.Title != null)
            subscription.Title = request.Title;
        if (request.SubgroupId != null)
            subscription.SubgroupId = request.SubgroupId;
        if (request.SubgroupName != null)
            subscription.SubgroupName = request.SubgroupName;
        if (request.KeywordInclude != null)
            subscription.KeywordInclude = request.KeywordInclude;
        if (request.KeywordExclude != null)
            subscription.KeywordExclude = request.KeywordExclude;
        if (request.IsEnabled.HasValue)
            subscription.IsEnabled = request.IsEnabled.Value;

        var updated = await _repository.UpdateSubscriptionAsync(subscription);
        return SubscriptionResponse.FromEntity(updated, _mikanConfig.BaseUrl);
    }

    public async Task<bool> DeleteSubscriptionAsync(int id)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return false;
        }

        await _repository.DeleteSubscriptionAsync(id);
        _logger.LogInformation("Deleted subscription {Id}", id);
        return true;
    }

    public async Task<SubscriptionResponse?> ToggleSubscriptionAsync(int id, bool enabled)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return null;
        }

        subscription.IsEnabled = enabled;
        var updated = await _repository.UpdateSubscriptionAsync(subscription);

        _logger.LogInformation("Subscription {Id} {Action}", id, enabled ? "enabled" : "disabled");
        return SubscriptionResponse.FromEntity(updated, _mikanConfig.BaseUrl);
    }

    public async Task<CheckSubscriptionResponse> CheckSubscriptionAsync(int id)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return new CheckSubscriptionResponse
            {
                SubscriptionId = id,
                Success = false,
                ErrorMessage = "Subscription not found"
            };
        }

        return await CheckSubscriptionInternalAsync(subscription);
    }

    public async Task<CheckAllSubscriptionsResponse> CheckAllSubscriptionsAsync()
    {
        var subscriptions = await _repository.GetEnabledSubscriptionsAsync();
        var response = new CheckAllSubscriptionsResponse
        {
            TotalSubscriptions = subscriptions.Count
        };

        // Limit the number of subscriptions per poll
        var toCheck = subscriptions.Take(_mikanConfig.MaxSubscriptionsPerPoll).ToList();

        foreach (var subscription in toCheck)
        {
            try
            {
                var result = await CheckSubscriptionInternalAsync(subscription);
                response.Results.Add(result);

                if (result.Success)
                {
                    response.SuccessCount++;
                    response.TotalNewItems += result.NewItemsCount;
                }
                else
                {
                    response.FailedCount++;
                }

                // Small delay between requests to avoid rate limiting
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subscription {Id}", subscription.Id);
                response.FailedCount++;
                response.Results.Add(new CheckSubscriptionResponse
                {
                    SubscriptionId = subscription.Id,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return response;
    }

    public async Task<List<DownloadHistoryResponse>> GetDownloadHistoryAsync(int subscriptionId, int limit = 50)
    {
        var history = await _repository.GetDownloadHistoryAsync(subscriptionId, limit);
        return history.Select(DownloadHistoryResponse.FromEntity).ToList();
    }

    public List<MikanRssItem> FilterRssItems(List<MikanRssItem> items, string? keywordInclude, string? keywordExclude)
    {
        var includeKeywords = ParseKeywords(keywordInclude);
        var excludeKeywords = ParseKeywords(keywordExclude);

        return items.Where(item =>
        {
            var title = item.Title.ToLower();

            // Check include keywords (all must match)
            if (includeKeywords.Count > 0)
            {
                if (!includeKeywords.All(k => title.Contains(k.ToLower())))
                {
                    return false;
                }
            }

            // Check exclude keywords (none should match)
            if (excludeKeywords.Count > 0)
            {
                if (excludeKeywords.Any(k => title.Contains(k.ToLower())))
                {
                    return false;
                }
            }

            return true;
        }).ToList();
    }

    #region Private Methods

    private async Task<CheckSubscriptionResponse> CheckSubscriptionInternalAsync(SubscriptionEntity subscription)
    {
        var result = new CheckSubscriptionResponse
        {
            SubscriptionId = subscription.Id
        };

        try
        {
            // Fetch RSS feed
            var feed = await _mikanClient.GetAnimeFeedAsync(
                subscription.MikanBangumiId,
                subscription.SubgroupId);

            _logger.LogDebug("Fetched {Count} items from RSS for subscription {Id}",
                feed.Items.Count, subscription.Id);

            // Filter by keywords
            var filteredItems = FilterRssItems(
                feed.Items,
                subscription.KeywordInclude,
                subscription.KeywordExclude);

            result.SkippedCount = feed.Items.Count - filteredItems.Count;

            // Check which items are already downloaded
            var newItems = new List<MikanRssItem>();
            foreach (var item in filteredItems)
            {
                if (string.IsNullOrEmpty(item.TorrentHash))
                {
                    _logger.LogWarning("Skipping item without hash: {Title}", item.Title);
                    result.SkippedCount++;
                    continue;
                }

                var exists = await _repository.ExistsDownloadByHashAsync(item.TorrentHash);
                if (exists)
                {
                    result.AlreadyDownloadedCount++;
                }
                else
                {
                    newItems.Add(item);
                }
            }

            // Process new items
            foreach (var item in newItems)
            {
                try
                {
                    // Create download history record
                    var history = new DownloadHistoryEntity
                    {
                        SubscriptionId = subscription.Id,
                        TorrentUrl = item.TorrentUrl,
                        TorrentHash = item.TorrentHash,
                        Title = item.Title,
                        FileSize = item.FileSize,
                        PublishedAt = item.PublishedAt,
                        Status = DownloadStatus.Pending
                    };

                    await _repository.CreateDownloadHistoryAsync(history);

                    // Add to qBittorrent
                    var torrentUrl = !string.IsNullOrEmpty(item.MagnetLink)
                        ? item.MagnetLink
                        : item.TorrentUrl;

                    var success = await _qbService.AddTorrentAsync(torrentUrl);

                    if (success)
                    {
                        history.Status = DownloadStatus.Downloading;
                        history.DownloadedAt = DateTime.UtcNow;
                        await _repository.UpdateDownloadHistoryAsync(history);

                        await _repository.UpdateSubscriptionLastDownloadAsync(subscription.Id);
                        await _repository.IncrementDownloadCountAsync(subscription.Id);

                        result.NewItemTitles.Add(item.Title);
                        _logger.LogInformation("Added torrent for {Title}", item.Title);
                    }
                    else
                    {
                        history.Status = DownloadStatus.Failed;
                        history.ErrorMessage = "Failed to add torrent to qBittorrent";
                        await _repository.UpdateDownloadHistoryAsync(history);
                        _logger.LogWarning("Failed to add torrent for {Title}", item.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing item {Title}", item.Title);
                }
            }

            result.NewItemsCount = result.NewItemTitles.Count;
            result.Success = true;

            // Update last checked time
            await _repository.UpdateSubscriptionLastCheckedAsync(subscription.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking subscription {Id}", subscription.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static List<string> ParseKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return new List<string>();
        }

        return keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();
    }

    #endregion
}
