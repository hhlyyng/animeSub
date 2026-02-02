using backend.Models;

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
}
