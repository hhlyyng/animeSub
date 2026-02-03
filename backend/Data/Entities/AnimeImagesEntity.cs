using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Entity for storing anime image URLs (poster, backdrop) from TMDB/AniList
/// </summary>
[Table("AnimeImages")]
public class AnimeImagesEntity
{
    [Key]
    public int BangumiId { get; set; }

    public string? PosterUrl { get; set; }      // Bangumi poster image
    public string? BackdropUrl { get; set; }    // TMDB backdrop/banner image
    public int? TmdbId { get; set; }            // TMDB ID for reference
    public int? AniListId { get; set; }         // AniList ID for reference

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Foreign key navigation
    [ForeignKey("BangumiId")]
    public AnimeInfoEntity? AnimeInfo { get; set; }
}
