namespace backend.Models;

/// <summary>
/// AniList anime information
/// </summary>
public class AniListAnimeInfo
{
    public string AnilistId { get; set; } = string.Empty;
    public string EnglishSummary { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string OriSiteUrl { get; set; } = string.Empty;
}
