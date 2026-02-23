using System.Text.Json.Serialization;

namespace backend.Models.Dtos;

/// <summary>
/// DTO for aggregated anime information from multiple sources
/// </summary>
public class AnimeInfoDto
{
    /// <summary>
    /// Bangumi subject ID
    /// </summary>
    [JsonPropertyName("bangumi_id")]
    public string BangumiId { get; set; } = string.Empty;

    /// <summary>
    /// Japanese title (contains hiragana/katakana)
    /// </summary>
    [JsonPropertyName("jp_title")]
    public string JpTitle { get; set; } = string.Empty;

    /// <summary>
    /// Chinese title
    /// </summary>
    [JsonPropertyName("ch_title")]
    public string ChTitle { get; set; } = string.Empty;

    /// <summary>
    /// English title (from TMDB or AniList)
    /// </summary>
    [JsonPropertyName("en_title")]
    public string EnTitle { get; set; } = string.Empty;

    /// <summary>
    /// Chinese description/summary
    /// </summary>
    [JsonPropertyName("ch_desc")]
    public string ChDesc { get; set; } = string.Empty;

    /// <summary>
    /// English description/summary (from TMDB or AniList)
    /// </summary>
    [JsonPropertyName("en_desc")]
    public string EnDesc { get; set; } = string.Empty;

    /// <summary>
    /// Bangumi rating score
    /// </summary>
    [JsonPropertyName("score")]
    public string Score { get; set; } = "0";

    /// <summary>
    /// Image URLs (portrait and landscape)
    /// </summary>
    [JsonPropertyName("images")]
    public AnimeImagesDto Images { get; set; } = new();

    /// <summary>
    /// External URLs
    /// </summary>
    [JsonPropertyName("external_urls")]
    public ExternalUrlsDto? ExternalUrls { get; set; }

    /// <summary>
    /// Mikan Bangumi ID for RSS feed URL construction
    /// Used to map anime to Mikan RSS feeds without searching
    /// </summary>
    [JsonPropertyName("mikan_bangumi_id")]
    public string? MikanBangumiId { get; set; }
}
