using backend.Models.Mikan;
using backend.Models.Dtos;

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

    /// <summary>
    /// Search for an anime on Mikan and return all seasons
    /// </summary>
    /// <param name="title">Anime title to search for</param>
    /// <returns>Search result with seasons, or null if not found</returns>
    Task<MikanSearchResult?> SearchAnimeAsync(string title);

    /// <summary>
    /// Get parsed RSS feed with metadata
    /// </summary>
    /// <param name="mikanBangumiId">Mikan anime ID</param>
    /// <returns>Feed response with parsed items and available filters</returns>
    Task<MikanFeedResponse> GetParsedFeedAsync(string mikanBangumiId);
}
