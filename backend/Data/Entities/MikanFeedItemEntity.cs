using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Cached parsed feed item for a Mikan Bangumi ID.
/// </summary>
[Table("MikanFeedItem")]
public class MikanFeedItemEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string MikanBangumiId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1024)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string TorrentUrl { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string MagnetLink { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TorrentHash { get; set; } = string.Empty;

    public bool CanDownload { get; set; }

    public long FileSize { get; set; }

    [MaxLength(64)]
    public string FormattedSize { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; }

    [MaxLength(32)]
    public string? Resolution { get; set; }

    [MaxLength(128)]
    public string? Subgroup { get; set; }

    [MaxLength(64)]
    public string? SubtitleType { get; set; }

    public int? Episode { get; set; }

    public bool IsCollection { get; set; }

    [ForeignKey(nameof(MikanBangumiId))]
    public MikanFeedCacheEntity? FeedCache { get; set; }
}
