namespace backend.Models;

/// <summary>
/// TMDB anime information
/// </summary>
public class TMDBAnimeInfo
{
    public string TMDBID { get; set; } = string.Empty;
    public string EnglishSummary { get; set; } = string.Empty;
    public string ChineseSummary { get; set; } = string.Empty;
    public string ChineseTitle { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    public string BackdropUrl { get; set; } = string.Empty;
    public string OriSiteUrl { get; set; } = string.Empty;
}
