using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Entity for storing complete aggregated anime information
/// Combines data from Bangumi, TMDB, and AniList APIs
/// </summary>
[Table("AnimeInfo")]
public class AnimeInfoEntity
{
    [Key]
    public int BangumiId { get; set; }

    // Titles
    public string? NameJapanese { get; set; }   // Original Japanese title (from Bangumi)
    public string? NameChinese { get; set; }    // Chinese title (from Bangumi)
    public string? NameEnglish { get; set; }    // English title (from TMDB/AniList)

    // Descriptions
    public string? DescChinese { get; set; }    // Chinese description (Bangumi or TMDB fallback)
    public string? DescEnglish { get; set; }    // English description (TMDB or AniList)

    // Rating
    public string? Score { get; set; }          // Score string (e.g., "8.5")

    // Images
    public string? ImagePortrait { get; set; }  // Portrait/poster image (from Bangumi)
    public string? ImageLandscape { get; set; } // Landscape/backdrop image (from TMDB)

    // External IDs
    public int? TmdbId { get; set; }
    public int? AnilistId { get; set; }

    // External URLs
    public string? UrlBangumi { get; set; }
    public string? UrlTmdb { get; set; }
    public string? UrlAnilist { get; set; }

    // Mikan integration
    /// <summary>
    /// Mikan Bangumi ID for RSS feed URL construction
    /// Used to map anime to Mikan RSS feeds without searching
    /// </summary>
    public string? MikanBangumiId { get; set; }

    // Schedule info
    public string? AirDate { get; set; }        // Original air date (YYYY-MM-DD)
    public int Weekday { get; set; }            // 1-7 (Monday-Sunday), 0 = unknown

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Data source tracking
    public bool IsPreFetched { get; set; }      // True if data was pre-fetched (complete)
}
