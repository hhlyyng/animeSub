using backend.Models.Mikan;

namespace backend.Services.Interfaces;

/// <summary>
/// Client interface for Mikan RSS service
/// </summary>
public interface IMikanClient
{
    /// <summary>
    /// Get RSS feed for a specific anime
    /// </summary>
    /// <param name="mikanBangumiId">Mikan anime ID</param>
    /// <param name="subgroupId">Optional subgroup ID to filter by specific release group</param>
    /// <returns>Parsed RSS feed</returns>
    Task<MikanRssFeed> GetAnimeFeedAsync(string mikanBangumiId, string? subgroupId = null);

    /// <summary>
    /// Build RSS URL for an anime subscription
    /// </summary>
    /// <param name="mikanBangumiId">Mikan anime ID</param>
    /// <param name="subgroupId">Optional subgroup ID</param>
    /// <returns>RSS URL</returns>
    string BuildRssUrl(string mikanBangumiId, string? subgroupId = null);
}
