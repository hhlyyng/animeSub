using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Data.Entities;

namespace backend.Services.Repositories;

/// <summary>
/// SQLite repository for subscription data persistence
/// </summary>
public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AnimeDbContext _context;
    private readonly ILogger<SubscriptionRepository> _logger;

    public SubscriptionRepository(
        AnimeDbContext context,
        ILogger<SubscriptionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Subscription CRUD

    public async Task<List<SubscriptionEntity>> GetAllSubscriptionsAsync()
    {
        return await _context.Subscriptions
            .Where(s => s.BangumiId > 0)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SubscriptionEntity>> GetEnabledSubscriptionsAsync()
    {
        return await _context.Subscriptions
            .Where(s => s.IsEnabled && s.BangumiId > 0)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<SubscriptionEntity?> GetSubscriptionByIdAsync(int id)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SubscriptionEntity?> GetSubscriptionByBangumiIdAsync(int bangumiId)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.BangumiId == bangumiId);
    }

    public async Task<SubscriptionEntity> CreateSubscriptionAsync(SubscriptionEntity subscription)
    {
        subscription.CreatedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created subscription {Id} for anime {Title} (BangumiId: {BangumiId})",
            subscription.Id, subscription.Title, subscription.BangumiId);

        return subscription;
    }

    public async Task<SubscriptionEntity> UpdateSubscriptionAsync(SubscriptionEntity subscription)
    {
        subscription.UpdatedAt = DateTime.UtcNow;

        _context.Subscriptions.Update(subscription);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated subscription {Id}", subscription.Id);

        return subscription;
    }

    public async Task DeleteSubscriptionAsync(int id)
    {
        var subscription = await _context.Subscriptions.FindAsync(id);
        if (subscription != null)
        {
            _context.Subscriptions.Remove(subscription);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted subscription {Id}", id);
        }
    }

    #endregion

    #region Download History

    public async Task<List<DownloadHistoryEntity>> GetDownloadHistoryAsync(int subscriptionId, int limit = 50)
    {
        return await _context.DownloadHistory
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.DiscoveredAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<DownloadHistoryEntity>> GetAllDownloadHistoryBySubscriptionIdAsync(int subscriptionId)
    {
        return await _context.DownloadHistory
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.DiscoveredAt)
            .ToListAsync();
    }

    public async Task<List<DownloadHistoryEntity>> GetManualDownloadHistoryByBangumiIdAsync(int bangumiId, int limit = 50)
    {
        return await _context.DownloadHistory
            .Where(d => d.Source == DownloadSource.Manual && d.AnimeBangumiId == bangumiId)
            .OrderByDescending(d => d.DiscoveredAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<DownloadHistoryEntity>> GetManualDownloadsWithAnimeContextAsync()
    {
        return await _context.DownloadHistory
            .Where(d => d.Source == DownloadSource.Manual && d.AnimeBangumiId.HasValue && d.AnimeBangumiId > 0)
            .OrderByDescending(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt)
            .ToListAsync();
    }

    public async Task<DownloadHistoryEntity?> GetDownloadByHashAsync(string torrentHash)
    {
        return await _context.DownloadHistory
            .FirstOrDefaultAsync(d => d.TorrentHash == torrentHash);
    }

    public async Task<bool> ExistsDownloadByHashAsync(string torrentHash)
    {
        return await _context.DownloadHistory
            .AnyAsync(d => d.TorrentHash == torrentHash);
    }

    public async Task<DownloadHistoryEntity> CreateDownloadHistoryAsync(DownloadHistoryEntity history)
    {
        history.DiscoveredAt = DateTime.UtcNow;

        _context.DownloadHistory.Add(history);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created download history for {Title} (Hash: {Hash})",
            history.Title, history.TorrentHash);

        return history;
    }

    public async Task<DownloadHistoryEntity> UpdateDownloadHistoryAsync(DownloadHistoryEntity history)
    {
        _context.DownloadHistory.Update(history);
        await _context.SaveChangesAsync();

        return history;
    }

    public async Task<List<DownloadHistoryEntity>> GetPendingDownloadsAsync()
    {
        return await _context.DownloadHistory
            .Where(d => d.Status == DownloadStatus.Pending)
            .OrderBy(d => d.DiscoveredAt)
            .ToListAsync();
    }

    #endregion

    #region Statistics

    public async Task UpdateSubscriptionLastCheckedAsync(int subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription != null)
        {
            subscription.LastCheckedAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateSubscriptionLastDownloadAsync(int subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription != null)
        {
            subscription.LastDownloadAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task IncrementDownloadCountAsync(int subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription != null)
        {
            subscription.DownloadCount++;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    #endregion
}
