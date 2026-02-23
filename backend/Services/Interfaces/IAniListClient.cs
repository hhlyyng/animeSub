using backend.Models;
using System.Text.Json;

namespace backend.Services.Interfaces;

/// <summary>
/// AniList GraphQL API client for fetching English anime metadata
/// </summary>
public interface IAniListClient
{
    /// <summary>
    /// Get anime information from AniList by Japanese title
    /// </summary>
    Task<AniListAnimeInfo?> GetAnimeInfoAsync(string japaneseTitle);

    /// <summary>
    /// Get trending anime from AniList
    /// </summary>
    /// <param name="limit">Number of anime to retrieve</param>
    /// <returns>List of trending anime</returns>
    Task<List<AniListAnimeInfo>> GetTrendingAnimeAsync(int limit = 10);

    /// <summary>
    /// Get anime sorted by popularity from AniList
    /// </summary>
    /// <param name="limit">Number of anime to retrieve</param>
    Task<List<AniListAnimeInfo>> GetAnimeByPopularityAsync(int limit = 50);

    /// <summary>
    /// Get anime sorted by score from AniList
    /// </summary>
    /// <param name="limit">Number of anime to retrieve</param>
    Task<List<AniListAnimeInfo>> GetAnimeByScoreAsync(int limit = 50);

    /// <summary>
    /// Search an anime media by title and return season/relations payload for episode offset inference.
    /// </summary>
    /// <param name="title">Anime title query</param>
    /// <returns>AniList Media Json payload, or null if unavailable</returns>
    Task<JsonElement?> SearchAnimeSeasonDataAsync(string title);

    /// <summary>
    /// Get an AniList media payload by AniList ID for relation-chain traversal.
    /// </summary>
    /// <param name="anilistId">AniList media ID</param>
    /// <returns>AniList Media Json payload, or null if unavailable</returns>
    Task<JsonElement?> GetAnimeSeasonDataByIdAsync(int anilistId);
}
