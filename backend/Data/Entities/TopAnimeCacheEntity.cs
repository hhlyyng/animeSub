using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Persistent cache snapshot for top anime lists (Bangumi/AniList/MAL).
/// Stores frontend-ready payload to avoid unnecessary external API calls.
/// </summary>
[Table("TopAnimeCache")]
public class TopAnimeCacheEntity
{
    [Key]
    [MaxLength(64)]
    public string Source { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = "[]";

    public DateTime UpdatedAt { get; set; }
}
