using System.Text.Json.Serialization;

namespace backend.Models.Dtos;

/// <summary>
/// DTO for external website URLs
/// </summary>
public class ExternalUrlsDto
{
    /// <summary>
    /// Bangumi website URL
    /// </summary>
    [JsonPropertyName("bangumi")]
    public string Bangumi { get; set; } = string.Empty;

    /// <summary>
    /// TMDB website URL
    /// </summary>
    [JsonPropertyName("tmdb")]
    public string Tmdb { get; set; } = string.Empty;

    /// <summary>
    /// AniList website URL
    /// </summary>
    [JsonPropertyName("anilist")]
    public string Anilist { get; set; } = string.Empty;

    /// <summary>
    /// MyAnimeList website URL
    /// </summary>
    [JsonPropertyName("mal")]
    public string Mal { get; set; } = string.Empty;
}
