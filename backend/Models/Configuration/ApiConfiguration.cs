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
