namespace backend.Data.Entities;

/// <summary>
/// Cached mapping of subgroup name to Mikan subgroup ID for a specific anime.
/// Populated when scraping the Mikan Bangumi page.
/// </summary>
public class MikanSubgroupEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Mikan bangumi ID this subgroup belongs to (e.g., "3503")
    /// </summary>
    public string MikanBangumiId { get; set; } = string.Empty;

    /// <summary>
    /// Mikan numeric subgroup ID (e.g., "1236")
    /// </summary>
    public string SubgroupId { get; set; } = string.Empty;

    /// <summary>
    /// Subgroup display name (e.g., "绿茶字幕组")
    /// </summary>
    public string SubgroupName { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
