using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Cached parsed feed header per Mikan Bangumi ID.
/// </summary>
[Table("MikanFeedCache")]
public class MikanFeedCacheEntity
{
    [Key]
    [MaxLength(64)]
    public string MikanBangumiId { get; set; } = string.Empty;

    [MaxLength(512)]
    public string SeasonName { get; set; } = string.Empty;

    public int? LatestEpisode { get; set; }

    public DateTime? LatestPublishedAt { get; set; }

    [MaxLength(1024)]
    public string? LatestTitle { get; set; }

    public int EpisodeOffset { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<MikanFeedItemEntity> Items { get; set; } = new();
}
