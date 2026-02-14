namespace backend.Models.Dtos;

/// <summary>
/// Result of searching for an anime on Mikan
/// </summary>
public class MikanSearchResult
{
    /// <summary>
    /// Anime title (Japanese)
    /// </summary>
    public string AnimeTitle { get; set; } = string.Empty;

    /// <summary>
    /// List of seasons found for this anime
    /// </summary>
    public List<MikanSeasonInfo> Seasons { get; set; } = new();

    /// <summary>
    /// Index of the default season to select (typically the latest)
    /// </summary>
    public int DefaultSeason { get; set; }
}

/// <summary>
/// Information about a specific season
/// </summary>
public class MikanSeasonInfo
{
    /// <summary>
    /// Season name (e.g., "Season 1", "Season 2")
    /// </summary>
    public string SeasonName { get; set; } = string.Empty;

    /// <summary>
    /// Mikan Bangumi ID for this season
    /// </summary>
    public string MikanBangumiId { get; set; } = string.Empty;

    /// <summary>
    /// Year the season aired
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// Parsed season number (1, 2, 3...) when available
    /// </summary>
    public int? SeasonNumber { get; set; }
}

/// <summary>
/// Response containing parsed RSS feed items
/// </summary>
public class MikanFeedResponse
{
    /// <summary>
    /// Season name
    /// </summary>
    public string SeasonName { get; set; } = string.Empty;

    /// <summary>
    /// List of parsed RSS items
    /// </summary>
    public List<ParsedRssItem> Items { get; set; } = new();

    /// <summary>
    /// List of available subgroups in the feed
    /// </summary>
    public List<string> AvailableSubgroups { get; set; } = new();

    /// <summary>
    /// List of available resolutions in the feed
    /// </summary>
    public List<string> AvailableResolutions { get; set; } = new();

    /// <summary>
    /// List of available subtitle types in the feed
    /// </summary>
    public List<string> AvailableSubtitleTypes { get; set; } = new();

    /// <summary>
    /// Latest episode number after normalization, if detected
    /// </summary>
    public int? LatestEpisode { get; set; }

    /// <summary>
    /// The latest publish time among feed items
    /// </summary>
    public DateTime? LatestPublishedAt { get; set; }

    /// <summary>
    /// Title of the latest feed item
    /// </summary>
    public string? LatestTitle { get; set; }

    /// <summary>
    /// Episode offset applied for renumbering (e.g., 24 means 25->1)
    /// </summary>
    public int EpisodeOffset { get; set; }
}

/// <summary>
/// Parsed torrent information extracted from RSS title
/// </summary>
public class ParsedTorrentInfo
{
    /// <summary>
    /// Normalized resolution (1080p, 720p, 4K, or null)
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Subgroup name (e.g., "ANi", "喵萌奶茶屋")
    /// </summary>
    public string? Subgroup { get; set; }

    /// <summary>
    /// Subtitle type (e.g., "简日内嵌", "繁日")
    /// </summary>
    public string? SubtitleType { get; set; }

    /// <summary>
    /// Episode number
    /// </summary>
    public int? Episode { get; set; }

    /// <summary>
    /// Whether this title represents a collection/batch package
    /// </summary>
    public bool IsCollection { get; set; }
}

/// <summary>
/// RSS item with parsed metadata
/// </summary>
public class ParsedRssItem
{
    /// <summary>
    /// Original RSS title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Torrent download URL
    /// </summary>
    public string TorrentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Magnet link
    /// </summary>
    public string MagnetLink { get; set; } = string.Empty;

    /// <summary>
    /// Torrent info hash
    /// </summary>
    public string TorrentHash { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item has enough normalized metadata to submit a download task
    /// </summary>
    public bool CanDownload { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Formatted file size string (e.g., "1.00 GB")
    /// </summary>
    public string FormattedSize { get; set; } = string.Empty;

    /// <summary>
    /// Publish date
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// Normalized resolution
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Subgroup name
    /// </summary>
    public string? Subgroup { get; set; }

    /// <summary>
    /// Subtitle type
    /// </summary>
    public string? SubtitleType { get; set; }

    /// <summary>
    /// Episode number
    /// </summary>
    public int? Episode { get; set; }

    /// <summary>
    /// Whether this item is a collection/batch package
    /// </summary>
    public bool IsCollection { get; set; }
}

/// <summary>
/// Request to download a torrent
/// </summary>
public class DownloadTorrentRequest
{
    /// <summary>
    /// Magnet link
    /// </summary>
    public string MagnetLink { get; set; } = string.Empty;

    /// <summary>
    /// Torrent URL
    /// </summary>
    public string? TorrentUrl { get; set; }

    /// <summary>
    /// Torrent title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Torrent info hash
    /// </summary>
    public string TorrentHash { get; set; } = string.Empty;

    /// <summary>
    /// Optional related anime Bangumi ID (used for manual download aggregation)
    /// </summary>
    public int? BangumiId { get; set; }

    /// <summary>
    /// Optional related anime Mikan ID (used for manual download aggregation)
    /// </summary>
    public string? MikanBangumiId { get; set; }

    /// <summary>
    /// Optional related anime title (used for manual download aggregation)
    /// </summary>
    public string? AnimeTitle { get; set; }
}
