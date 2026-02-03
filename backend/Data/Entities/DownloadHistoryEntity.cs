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
    /// When the torrent was pushed to qBittorrent
    /// </summary>
    public DateTime? DownloadedAt { get; set; }

    /// <summary>
    /// Navigation property to subscription
    /// </summary>
    [ForeignKey(nameof(SubscriptionId))]
    public SubscriptionEntity? Subscription { get; set; }
}
