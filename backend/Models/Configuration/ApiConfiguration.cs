namespace backend.Models.Configuration;

/// <summary>
/// Root configuration for all external API clients
/// </summary>
public class ApiConfiguration
{
    public const string SectionName = "ApiConfiguration";

    public BangumiConfig Bangumi { get; set; } = new();
    public TMDBConfig TMDB { get; set; } = new();
    public AniListConfig AniList { get; set; } = new();
}

/// <summary>
/// Bangumi API configuration
/// </summary>
public class BangumiConfig
{
    /// <summary>
    /// Base URL for Bangumi API (default: https://api.bgm.tv)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.bgm.tv";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// TMDB API configuration
/// </summary>
public class TMDBConfig
{
    /// <summary>
    /// Base URL for TMDB API (default: https://api.themoviedb.org/3)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3";

    /// <summary>
    /// Base URL for TMDB images (default: https://image.tmdb.org/t/p/)
    /// </summary>
    public string ImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p/";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// AniList API configuration
/// </summary>
public class AniListConfig
{
    /// <summary>
    /// GraphQL endpoint for AniList (default: https://graphql.anilist.co)
    /// </summary>
    public string BaseUrl { get; set; } = "https://graphql.anilist.co";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Pre-fetch service configuration
/// </summary>
public class PreFetchConfig
{
    public const string SectionName = "PreFetch";

    /// <summary>
    /// Enable/disable the pre-fetch background service
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hour of day to run pre-fetch (0-23, default: 3 = 3:00 AM)
    /// </summary>
    public int ScheduleHour { get; set; } = 3;

    /// <summary>
    /// Run pre-fetch immediately on application startup
    /// </summary>
    public bool RunOnStartup { get; set; } = false;

    /// <summary>
    /// Maximum concurrent API requests during pre-fetch
    /// </summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Bangumi API token for pre-fetch service
    /// </summary>
    public string BangumiToken { get; set; } = "";

    /// <summary>
    /// TMDB API token for pre-fetch service
    /// </summary>
    public string TmdbToken { get; set; } = "";
}
