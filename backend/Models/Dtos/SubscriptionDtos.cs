using backend.Data.Entities;

namespace backend.Models.Dtos;

/// <summary>
/// Request DTO for creating a new subscription
/// </summary>
public class CreateSubscriptionRequest
{
    /// <summary>
    /// Bangumi anime ID
    /// </summary>
    public int BangumiId { get; set; }

    /// <summary>
    /// Anime title for display
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mikan anime ID (from Mikan website URL)
    /// </summary>
    public string MikanBangumiId { get; set; } = string.Empty;

    /// <summary>
    /// Optional subgroup ID to filter by specific release group
    /// </summary>
    public string? SubgroupId { get; set; }

    /// <summary>
    /// Subgroup name for display
    /// </summary>
    public string? SubgroupName { get; set; }

    /// <summary>
    /// Keywords that must be included (comma-separated)
    /// Example: "1080p,简体" to only download 1080p with simplified Chinese
    /// </summary>
    public string? KeywordInclude { get; set; }

    /// <summary>
    /// Keywords to exclude (comma-separated)
    /// Example: "HEVC,x265" to exclude HEVC encoded releases
    /// </summary>
    public string? KeywordExclude { get; set; }
}

/// <summary>
/// Request DTO for updating a subscription
/// </summary>
public class UpdateSubscriptionRequest
{
    public string? Title { get; set; }
    public string? SubgroupId { get; set; }
    public string? SubgroupName { get; set; }
    public string? KeywordInclude { get; set; }
    public string? KeywordExclude { get; set; }
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// Request DTO for canceling a subscription
/// </summary>
public class CancelSubscriptionRequest
{
    /// <summary>
    /// Cancel action: delete_files or keep_files
    /// </summary>
    public string Action { get; set; } = CancelSubscriptionActionValues.KeepFiles;
}

/// <summary>
/// Allowed action values for canceling subscription
/// </summary>
public static class CancelSubscriptionActionValues
{
    public const string DeleteFiles = "delete_files";
    public const string KeepFiles = "keep_files";
}

/// <summary>
/// Response DTO for cancel subscription action
/// </summary>
public class CancelSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public bool IsEnabled { get; set; }
    public string Action { get; set; } = CancelSubscriptionActionValues.KeepFiles;
    public int TotalTorrents { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
}

/// <summary>
/// Response DTO for subscription information
/// </summary>
public class SubscriptionResponse
{
    public int Id { get; set; }
    public int BangumiId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MikanBangumiId { get; set; } = string.Empty;
    public string? SubgroupId { get; set; }
    public string? SubgroupName { get; set; }
    public string? KeywordInclude { get; set; }
    public string? KeywordExclude { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastDownloadAt { get; set; }
    public int DownloadCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Constructed RSS URL for this subscription
    /// </summary>
    public string RssUrl { get; set; } = string.Empty;

    public static SubscriptionResponse FromEntity(SubscriptionEntity entity, string baseUrl)
    {
        var rssUrl = $"{baseUrl}/RSS/Bangumi?bangumiId={entity.MikanBangumiId}";
        if (!string.IsNullOrEmpty(entity.SubgroupId))
        {
            rssUrl += $"&subgroupid={entity.SubgroupId}";
        }

        return new SubscriptionResponse
        {
            Id = entity.Id,
            BangumiId = entity.BangumiId,
            Title = entity.Title,
            MikanBangumiId = entity.MikanBangumiId,
            SubgroupId = entity.SubgroupId,
            SubgroupName = entity.SubgroupName,
            KeywordInclude = entity.KeywordInclude,
            KeywordExclude = entity.KeywordExclude,
            IsEnabled = entity.IsEnabled,
            LastCheckedAt = entity.LastCheckedAt,
            LastDownloadAt = entity.LastDownloadAt,
            DownloadCount = entity.DownloadCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            RssUrl = rssUrl
        };
    }
}

/// <summary>
/// Response DTO for download history item
/// </summary>
public class DownloadHistoryResponse
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public string TorrentUrl { get; set; } = string.Empty;
    public string TorrentHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public DateTime? DownloadedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public double Progress { get; set; }
    public long? DownloadSpeed { get; set; }
    public int? Eta { get; set; }
    public int? NumSeeds { get; set; }
    public int? NumLeechers { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Human-readable file size
    /// </summary>
    public string? FileSizeDisplay { get; set; }

    public static DownloadHistoryResponse FromEntity(DownloadHistoryEntity entity)
    {
        return new DownloadHistoryResponse
        {
            Id = entity.Id,
            SubscriptionId = entity.SubscriptionId,
            TorrentUrl = entity.TorrentUrl,
            TorrentHash = entity.TorrentHash,
            Title = entity.Title,
            FileSize = entity.FileSize,
            Status = entity.Status.ToString(),
            ErrorMessage = entity.ErrorMessage,
            PublishedAt = entity.PublishedAt,
            DiscoveredAt = entity.DiscoveredAt,
            DownloadedAt = entity.DownloadedAt,
            Source = entity.Source.ToString(),
            Progress = entity.Progress,
            DownloadSpeed = entity.DownloadSpeed,
            Eta = entity.Eta,
            NumSeeds = entity.NumSeeds,
            NumLeechers = entity.NumLeechers,
            LastSyncedAt = entity.LastSyncedAt,
            FileSizeDisplay = FormatFileSize(entity.FileSize)
        };
    }

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes == null) return null;

        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes.Value;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Response DTO for anime that has manual download tasks but is not subscribed
/// </summary>
public class ManualDownloadAnimeResponse
{
    public int BangumiId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? MikanBangumiId { get; set; }
    public int TaskCount { get; set; }
    public DateTime LastTaskAt { get; set; }
}

/// <summary>
/// Lightweight DTO returning only hash + metadata for a download task.
/// The frontend uses this to correlate with qBittorrent polling data.
/// </summary>
public class TaskHashResponse
{
    public string Hash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public long? FileSize { get; set; }
    public bool IsCompleted { get; set; }

    public static TaskHashResponse FromEntity(Data.Entities.DownloadHistoryEntity entity)
    {
        return new TaskHashResponse
        {
            Hash = entity.TorrentHash,
            Title = entity.Title,
            PublishedAt = entity.PublishedAt,
            FileSize = entity.FileSize,
            IsCompleted = entity.Status == Data.Entities.DownloadStatus.Completed
        };
    }
}

/// <summary>
/// Response DTO for subscription check result
/// </summary>
public class CheckSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int NewItemsCount { get; set; }
    public int SkippedCount { get; set; }
    public int AlreadyDownloadedCount { get; set; }
    public List<string> NewItemTitles { get; set; } = new();
}

/// <summary>
/// Response DTO for check all subscriptions result
/// </summary>
public class CheckAllSubscriptionsResponse
{
    public int TotalSubscriptions { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalNewItems { get; set; }
    public List<CheckSubscriptionResponse> Results { get; set; } = new();
}
