using backend.Models;

namespace backend.Services.Interfaces;

/// <summary>
/// TMDB API client for fetching English translations and backdrop images
/// </summary>
public interface ITMDBClient
{
    /// <summary>
    /// Get anime summary and backdrop URL from TMDB by title
    /// </summary>
    Task<TMDBAnimeInfo?> GetAnimeSummaryAndBackdropAsync(string title);

    /// <summary>
    /// Get movie images (posters, backdrops, logos)
    /// </summary>
    Task<List<string>> GetMovieImagesAsync(int movieId, string imageType = "posters", string size = "w500", string language = "en-US");

    /// <summary>
    /// Get TV show images
    /// </summary>
    Task<List<string>> GetTVImagesAsync(int tvId, string imageType = "posters", string size = "w500", string language = "en-US");

    /// <summary>
    /// Get person images
    /// </summary>
    Task<List<string>> GetPersonImagesAsync(int personId, string size = "w185");

    /// <summary>
    /// Set the API authentication token (optional)
    /// </summary>
    void SetToken(string? token);
}
