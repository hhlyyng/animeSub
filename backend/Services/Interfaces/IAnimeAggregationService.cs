using backend.Models;

namespace backend.Services.Interfaces;

/// <summary>
/// Service that aggregates anime data from multiple external APIs
/// </summary>
public interface IAnimeAggregationService
{
    /// <summary>
    /// Get today's anime with enriched data from multiple sources
    /// Returns response with data source indicator for frontend
    /// </summary>
    /// <param name="bangumiToken">Bangumi API token (required)</param>
    /// <param name="tmdbToken">TMDB API token (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with anime list and metadata (data source, staleness, etc.)</returns>
    Task<AnimeListResponse> GetTodayAnimeEnrichedAsync(
        string bangumiToken,
        string? tmdbToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top anime from Bangumi rankings
    /// </summary>
    /// <param name="bangumiToken">Bangumi API token</param>
    /// <param name="limit">Number of anime to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<AnimeListResponse> GetTopAnimeFromBangumiAsync(
        string bangumiToken,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get trending anime from AniList
    /// </summary>
    /// <param name="limit">Number of anime to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<AnimeListResponse> GetTopAnimeFromAniListAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top anime from MyAnimeList (via Jikan API)
    /// </summary>
    /// <param name="limit">Number of anime to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<AnimeListResponse> GetTopAnimeFromMALAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);
}
