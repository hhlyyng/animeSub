using backend.Models.Dtos;

namespace backend.Models;

/// <summary>
/// Data source indicator for anime responses
/// </summary>
public enum DataSource
{
    /// <summary>
    /// Fresh data from external API (Bangumi/TMDB)
    /// </summary>
    Api,

    /// <summary>
    /// Cached data from memory cache
    /// </summary>
    Cache,

    /// <summary>
    /// Pre-fetched data from SQLite database
    /// </summary>
    Database,

    /// <summary>
    /// Fallback data from cache due to API failure
    /// </summary>
    CacheFallback
}

/// <summary>
/// Response wrapper with metadata about data source
/// </summary>
public class AnimeListResponse
{
    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Data source indicator
    /// </summary>
    public DataSource DataSource { get; set; }

    /// <summary>
    /// Whether the API request failed and we're using cached data
    /// </summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Human-readable message about the response
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of when the data was last updated
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Number of anime items
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// The anime data (strongly typed)
    /// </summary>
    public List<AnimeInfoDto> Animes { get; set; } = new();

    /// <summary>
    /// Number of retry attempts made (if any)
    /// </summary>
    public int RetryAttempts { get; set; }
}
