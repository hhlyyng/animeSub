using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Download status enumeration
/// </summary>
public enum DownloadStatus
{
    Pending = 0,
    Downloading = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// Entity for storing download history of subscriptions
/// </summary>
[Table("DownloadHistory")]
public class DownloadHistoryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to subscription
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Torrent download URL
    /// </summary>
    [Required]
    public string TorrentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Torrent info hash (unique identifier for deduplication)
    /// </summary>
    [Required]
    public string TorrentHash { get; set; } = string.Empty;

    /// <summary>
    /// Torrent/resource title
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes (if available)
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Download status
    /// </summary>
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the torrent was published in RSS
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// When the torrent was discovered by polling
    /// </summary>
    public DateTime DiscoveredAt { get; set; }

    /// <summary>
    /// When torrent was pushed to qBittorrent
    /// </summary>
    public DateTime? DownloadedAt { get; set; }

    /// <summary>
    /// Download source: Manual (user clicked) or Subscription (auto polling)
    /// </summary>
    public DownloadSource Source { get; set; } = DownloadSource.Manual;

    /// <summary>
    /// Related anime Bangumi ID for UI-level grouping (especially manual downloads)
    /// </summary>
    public int? AnimeBangumiId { get; set; }

    /// <summary>
    /// Related anime Mikan Bangumi ID for UI-level linking
    /// </summary>
    public string? AnimeMikanBangumiId { get; set; }

    /// <summary>
    /// Related anime display title for UI fallback
    /// </summary>
    public string? AnimeTitle { get; set; }

    /// <summary>
    /// Current download progress (0-100)
    /// </summary>
    public double Progress { get; set; } = 0;

    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public long? DownloadSpeed { get; set; }

    /// <summary>
    /// ETA in seconds
    /// </summary>
    public int? Eta { get; set; }

    /// <summary>
    /// Number of seeders
    /// </summary>
    public int? NumSeeds { get; set; }

    /// <summary>
    /// Number of leechers
    /// </summary>
    public int? NumLeechers { get; set; }

    /// <summary>
    /// Last synced with qBittorrent at
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Save path (from qBittorrent)
    /// </summary>
    public string? SavePath { get; set; }

    /// <summary>
    /// qBittorrent category
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Navigation property to subscription
    /// </summary>
    [ForeignKey(nameof(SubscriptionId))]
    public SubscriptionEntity? Subscription { get; set; }
}
