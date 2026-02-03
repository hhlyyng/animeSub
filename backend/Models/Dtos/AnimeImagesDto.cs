using System.Text.Json.Serialization;

namespace backend.Models.Dtos;

/// <summary>
/// DTO for anime images (portrait and landscape)
/// </summary>
public class AnimeImagesDto
{
    /// <summary>
    /// Portrait image URL (from Bangumi)
    /// </summary>
    [JsonPropertyName("portrait")]
    public string Portrait { get; set; } = string.Empty;

    /// <summary>
    /// Landscape/backdrop image URL (from TMDB)
    /// </summary>
    [JsonPropertyName("landscape")]
    public string Landscape { get; set; } = string.Empty;
}
