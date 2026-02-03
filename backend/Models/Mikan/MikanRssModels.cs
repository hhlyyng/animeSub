namespace backend.Models.Mikan;

/// <summary>
/// Represents a single RSS item from Mikan
/// </summary>
public class MikanRssItem
{
    /// <summary>
    /// Resource title (usually contains anime name, episode, quality, subgroup)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Torrent download URL
    /// </summary>
    public string TorrentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Torrent info hash (extracted from enclosure or magnet link)
    /// </summary>
    public string TorrentHash { get; set; } = string.Empty;

    /// <summary>
    /// Magnet link (if available)
    /// </summary>
    public string? MagnetLink { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Publish date from RSS
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// Link to the resource page on Mikan
    /// </summary>
    public string? Link { get; set; }
}

/// <summary>
/// Represents an RSS feed response from Mikan
/// </summary>
public class MikanRssFeed
{
    /// <summary>
    /// Feed title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Feed description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Feed link
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    /// RSS items
    /// </summary>
    public List<MikanRssItem> Items { get; set; } = new();
}

/// <summary>
/// Result of checking a subscription for new torrents
/// </summary>
public class SubscriptionCheckResult
{
    /// <summary>
    /// Subscription ID that was checked
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Whether the check was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if check failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// New items found that weren't previously downloaded
    /// </summary>
    public List<MikanRssItem> NewItems { get; set; } = new();

    /// <summary>
    /// Number of items skipped due to filters
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Number of items already downloaded
    /// </summary>
    public int AlreadyDownloadedCount { get; set; }
}
