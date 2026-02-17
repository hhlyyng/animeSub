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

    public async Task<SubscriptionResponse?> GetSubscriptionByBangumiIdAsync(int bangumiId)
    {
        if (bangumiId <= 0)
        {
            return null;
        }

        var subscription = await _repository.GetSubscriptionByBangumiIdAsync(bangumiId);
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

    public async Task<SubscriptionResponse> EnsureSubscriptionAsync(CreateSubscriptionRequest request)
    {
        if (request.BangumiId <= 0)
        {
            throw new ArgumentException("Valid BangumiId is required", nameof(request.BangumiId));
        }

        // Defensive: warn when SubgroupName is provided without a SubgroupId,
        // which would cause the subscription to fall back to unfiltered RSS.
        if (!string.IsNullOrWhiteSpace(request.SubgroupName) &&
            string.IsNullOrWhiteSpace(request.SubgroupId))
        {
            _logger.LogWarning(
                "EnsureSubscription for BangumiId {BangumiId}: SubgroupName='{SubgroupName}' provided without SubgroupId — " +
                "subscription will use unfiltered RSS feed, relying solely on keyword filtering",
                request.BangumiId, request.SubgroupName);
        }

        var existing = await _repository.GetSubscriptionByBangumiIdAsync(request.BangumiId);
        if (existing == null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException("Title is required", nameof(request.Title));
            }

            if (string.IsNullOrWhiteSpace(request.MikanBangumiId))
            {
                throw new ArgumentException("MikanBangumiId is required", nameof(request.MikanBangumiId));
            }

            return await CreateSubscriptionAsync(request);
        }

        var hasChanges = false;
        if (!existing.IsEnabled)
        {
            existing.IsEnabled = true;
            hasChanges = true;

            // Clear old download history from previous subscription period
            await _repository.ClearDownloadHistoryAsync(existing.Id);
            existing.DownloadCount = 0;
        }

        if (!string.IsNullOrWhiteSpace(request.MikanBangumiId) &&
            !string.Equals(existing.MikanBangumiId, request.MikanBangumiId, StringComparison.Ordinal))
        {
            existing.MikanBangumiId = request.MikanBangumiId.Trim();
            hasChanges = true;
        }

        if (!string.IsNullOrWhiteSpace(request.Title) &&
            !string.Equals(existing.Title, request.Title, StringComparison.Ordinal))
        {
            existing.Title = request.Title.Trim();
            hasChanges = true;
        }

        // SubgroupId: null = no change, "" = clear, "xxx" = update
        if (request.SubgroupId != null)
        {
            var newValue = string.IsNullOrWhiteSpace(request.SubgroupId) ? null : request.SubgroupId.Trim();
            if (!string.Equals(existing.SubgroupId, newValue, StringComparison.Ordinal))
            {
                existing.SubgroupId = newValue;
                hasChanges = true;
            }
        }

        // SubgroupName: null = no change, "" = clear, "xxx" = update
        if (request.SubgroupName != null)
        {
            var newValue = string.IsNullOrWhiteSpace(request.SubgroupName) ? null : request.SubgroupName.Trim();
            if (!string.Equals(existing.SubgroupName, newValue, StringComparison.Ordinal))
            {
                existing.SubgroupName = newValue;
                hasChanges = true;
            }
        }

        // KeywordInclude: null = no change, "" = clear, "xxx" = update
        if (request.KeywordInclude != null)
        {
            var newValue = string.IsNullOrWhiteSpace(request.KeywordInclude) ? null : request.KeywordInclude.Trim();
            if (!string.Equals(existing.KeywordInclude, newValue, StringComparison.Ordinal))
            {
                existing.KeywordInclude = newValue;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            existing = await _repository.UpdateSubscriptionAsync(existing);
        }

        return SubscriptionResponse.FromEntity(existing, _mikanConfig.BaseUrl);
    }

    public async Task<CancelSubscriptionResponse?> CancelSubscriptionAsync(int subscriptionId, bool deleteFiles)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(subscriptionId);
        if (subscription == null)
        {
            return null;
        }

        var history = await _repository.GetAllDownloadHistoryBySubscriptionIdAsync(subscriptionId);
        var hashes = history
            .Select(item => item.TorrentHash?.Trim().ToUpperInvariant())
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var processedCount = 0;
        var failedCount = 0;

        foreach (var hash in hashes)
        {
            try
            {
                if (deleteFiles)
                {
                    var removed = await _qbService.DeleteTorrentAsync(hash!, true);
                    if (!removed)
                    {
                        failedCount++;
                        continue;
                    }
                }
                else
                {
                    await _qbService.PauseTorrentAsync(hash!);
                }

                processedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogWarning(
                    ex,
                    "Failed to {Operation} torrent {Hash} while canceling subscription {SubscriptionId}",
                    deleteFiles ? "delete" : "pause",
                    hash,
                    subscriptionId);
            }
        }

        if (subscription.IsEnabled)
        {
            subscription.IsEnabled = false;
            await _repository.UpdateSubscriptionAsync(subscription);
        }

        return new CancelSubscriptionResponse
        {
            SubscriptionId = subscriptionId,
            IsEnabled = subscription.IsEnabled,
            Action = deleteFiles ? CancelSubscriptionActionValues.DeleteFiles : CancelSubscriptionActionValues.KeepFiles,
            TotalTorrents = hashes.Count,
            ProcessedCount = processedCount,
            FailedCount = failedCount
        };
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

    public async Task<List<ManualDownloadAnimeResponse>> GetManualOnlyDownloadAnimesAsync(int limit = 200)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var subscriptions = await _repository.GetAllSubscriptionsAsync();
        var subscribedBangumiIds = subscriptions
            .Select(s => s.BangumiId)
            .ToHashSet();

        var manualDownloads = await _repository.GetManualDownloadsWithAnimeContextAsync();

        var grouped = manualDownloads
            .Where(d => d.AnimeBangumiId.HasValue && d.AnimeBangumiId.Value > 0)
            .Where(d => !subscribedBangumiIds.Contains(d.AnimeBangumiId!.Value))
            .GroupBy(d => d.AnimeBangumiId!.Value)
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt)
                    .First();

                var title = ResolveManualAnimeTitle(group, latest);
                var mikanBangumiId = group
                    .Select(d => d.AnimeMikanBangumiId)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var lastTaskAt = group
                    .Max(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt);

                return new ManualDownloadAnimeResponse
                {
                    BangumiId = group.Key,
                    Title = title,
                    MikanBangumiId = mikanBangumiId,
                    TaskCount = group.Count(),
                    LastTaskAt = lastTaskAt
                };
            })
            .OrderByDescending(item => item.LastTaskAt)
            .Take(safeLimit)
            .ToList();

        return grouped;
    }

    public async Task<List<DownloadHistoryResponse>> GetManualDownloadHistoryAsync(int bangumiId, int limit = 50)
    {
        if (bangumiId <= 0)
        {
            return new List<DownloadHistoryResponse>();
        }

        var safeLimit = Math.Clamp(limit, 1, 500);
        var history = await _repository.GetManualDownloadHistoryByBangumiIdAsync(bangumiId, safeLimit);
        return history.Select(DownloadHistoryResponse.FromEntity).ToList();
    }

    public async Task<List<TaskHashResponse>> GetTaskHashesAsync(int subscriptionId, int limit = 300)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var history = await _repository.GetDownloadHistoryAsync(subscriptionId, safeLimit);
        return history.Select(TaskHashResponse.FromEntity).ToList();
    }

    public async Task<List<TaskHashResponse>> GetManualTaskHashesAsync(int bangumiId, int limit = 300)
    {
        if (bangumiId <= 0)
        {
            return new List<TaskHashResponse>();
        }

        var safeLimit = Math.Clamp(limit, 1, 500);
        var history = await _repository.GetManualDownloadHistoryByBangumiIdAsync(bangumiId, safeLimit);
        return history.Select(TaskHashResponse.FromEntity).ToList();
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

            _logger.LogInformation(
                "[Polling] Sub#{Id} '{Title}' — RSS returned {Count} items (MikanId={MikanId}, SubgroupId={SubgroupId}, KeywordInclude={KW})",
                subscription.Id, subscription.Title, feed.Items.Count,
                subscription.MikanBangumiId, subscription.SubgroupId ?? "(null)", subscription.KeywordInclude ?? "(null)");

            if (string.IsNullOrEmpty(subscription.SubgroupId))
            {
                _logger.LogWarning(
                    "[Polling] Sub#{Id} has NO SubgroupId — RSS returns ALL subgroups, relying solely on keyword filtering!",
                    subscription.Id);
            }

            // Filter by keywords
            var filteredItems = FilterRssItems(
                feed.Items,
                subscription.KeywordInclude,
                subscription.KeywordExclude);

            var skippedItems = feed.Items.Except(filteredItems).ToList();
            if (skippedItems.Count > 0)
            {
                _logger.LogInformation("[Polling] Sub#{Id} — Filtered OUT {Count} items:", subscription.Id, skippedItems.Count);
                foreach (var sk in skippedItems.Take(5))
                    _logger.LogInformation("[Polling]   SKIP: {Title}", sk.Title);
            }

            _logger.LogInformation("[Polling] Sub#{Id} — {Count} items passed keyword filter", subscription.Id, filteredItems.Count);

            result.SkippedCount = feed.Items.Count - filteredItems.Count;

            // Check which items are already downloaded (batch query instead of N+1)
            var candidateItems = new List<MikanRssItem>();
            foreach (var item in filteredItems)
            {
                if (string.IsNullOrEmpty(item.TorrentHash))
                {
                    _logger.LogWarning("Skipping item without hash: {Title}", item.Title);
                    result.SkippedCount++;
                    continue;
                }
                candidateItems.Add(item);
            }

            var candidateHashes = candidateItems.Select(i => i.TorrentHash!).ToList();
            var existingHashes = await _repository.GetExistingHashesAsync(candidateHashes);

            var newItems = new List<MikanRssItem>();
            foreach (var item in candidateItems)
            {
                if (existingHashes.Contains(item.TorrentHash!))
                {
                    result.AlreadyDownloadedCount++;
                }
                else
                {
                    newItems.Add(item);
                }
            }

            if (newItems.Count > 0)
            {
                _logger.LogInformation("[Polling] Sub#{Id} — {Count} NEW items will be pushed to qBittorrent:", subscription.Id, newItems.Count);
                foreach (var ni in newItems)
                    _logger.LogInformation("[Polling]   NEW: {Title} (hash={Hash})", ni.Title, ni.TorrentHash?[..Math.Min(8, ni.TorrentHash.Length)]);
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
                        AnimeBangumiId = subscription.BangumiId,
                        AnimeMikanBangumiId = subscription.MikanBangumiId,
                        AnimeTitle = subscription.Title,
                        FileSize = item.FileSize,
                        PublishedAt = item.PublishedAt,
                        Status = DownloadStatus.Pending,
                        Source = DownloadSource.Subscription
                    };

                    await _repository.CreateDownloadHistoryAsync(history);

                    // Add to qBittorrent
                    var torrentUrl = !string.IsNullOrEmpty(item.MagnetLink)
                        ? item.MagnetLink
                        : item.TorrentUrl;

                    var success = await _qbService.AddTorrentWithTrackingAsync(
                        torrentUrl,
                        item.TorrentHash,
                        item.Title,
                        item.FileSize ?? 0,
                        DownloadSource.Subscription,
                        subscription.Id);

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking subscription {Id}", subscription.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            // Always update LastCheckedAt so a persistently failing subscription
            // rotates to the back of the queue instead of starving others.
            try
            {
                await _repository.UpdateSubscriptionLastCheckedAsync(subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update LastCheckedAt for subscription {Id}", subscription.Id);
            }
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

    private static string ResolveManualAnimeTitle(
        IGrouping<int, DownloadHistoryEntity> group,
        DownloadHistoryEntity latest)
    {
        if (!string.IsNullOrWhiteSpace(latest.AnimeTitle))
        {
            return latest.AnimeTitle.Trim();
        }

        var fallback = group
            .Select(item => item.AnimeTitle)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        if (!string.IsNullOrWhiteSpace(latest.Title))
        {
            return latest.Title.Trim();
        }

        return $"Bangumi {group.Key}";
    }

    #endregion
}
