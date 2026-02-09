using backend.Models.Jikan;
using System.Text.Json;

namespace backend.Services.Interfaces;

/// <summary>
/// Jikan API client for fetching anime data from MyAnimeList
/// </summary>
public interface IJikanClient
{
    /// <summary>
    /// Get top anime list from MyAnimeList via Jikan API
    /// </summary>
    /// <param name="limit">Number of anime to retrieve (max 25)</param>
    /// <returns>List of top anime</returns>
    Task<List<JikanAnimeInfo>> GetTopAnimeAsync(int limit = 10);

    /// <summary>
    /// Get anime detail payload (contains episodes count) by MAL ID.
    /// </summary>
    /// <param name="malId">MyAnimeList anime ID</param>
    /// <returns>Jikan data payload, or null if unavailable</returns>
    Task<JsonElement?> GetAnimeDetailAsync(int malId);
}
