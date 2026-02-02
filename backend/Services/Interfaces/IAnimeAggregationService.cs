namespace backend.Services.Interfaces;

/// <summary>
/// Service that aggregates anime data from multiple external APIs
/// </summary>
public interface IAnimeAggregationService
{
    /// <summary>
    /// Get today's anime with enriched data from multiple sources
    /// </summary>
    /// <param name="bangumiToken">Bangumi API token (required)</param>
    /// <param name="tmdbToken">TMDB API token (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of enriched anime information</returns>
    Task<List<object>> GetTodayAnimeEnrichedAsync(
        string bangumiToken,
        string? tmdbToken = null,
        CancellationToken cancellationToken = default);
}
