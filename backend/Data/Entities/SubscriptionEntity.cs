using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Entity for storing anime subscription information
/// </summary>
[Table("Subscriptions")]
public class SubscriptionEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Bangumi anime ID
    /// </summary>
    public int BangumiId { get; set; }

    /// <summary>
    /// Anime title for display
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mikan anime ID (used for RSS URL construction)
    /// </summary>
    [Required]
    public string MikanBangumiId { get; set; } = string.Empty;

    /// <summary>
    /// Optional subgroup ID for filtering specific release groups
    /// </summary>
    public string? SubgroupId { get; set; }

    /// <summary>
    /// Subgroup name for display
    /// </summary>
    public string? SubgroupName { get; set; }

    /// <summary>
    /// Comma-separated keywords that must be included in torrent title
    /// </summary>
    public string? KeywordInclude { get; set; }

    /// <summary>
    /// Comma-separated keywords that must be excluded from torrent title
    /// </summary>
    public string? KeywordExclude { get; set; }

    /// <summary>
    /// Whether this subscription is active
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Last time RSS was checked for this subscription
    /// </summary>
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>
    /// Last time a torrent was downloaded for this subscription
    /// </summary>
    public DateTime? LastDownloadAt { get; set; }

    /// <summary>
    /// Total number of episodes downloaded
    /// </summary>
    public int DownloadCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property for download history
    /// </summary>
    public ICollection<DownloadHistoryEntity> DownloadHistory { get; set; } = new List<DownloadHistoryEntity>();
}
