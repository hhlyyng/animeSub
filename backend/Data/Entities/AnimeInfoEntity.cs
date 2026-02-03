using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Entity for storing basic anime information from Bangumi API
/// </summary>
[Table("AnimeInfo")]
public class AnimeInfoEntity
{
    [Key]
    public int BangumiId { get; set; }

    public string? NameChinese { get; set; }
    public string? NameJapanese { get; set; }
    public string? NameEnglish { get; set; }
    public double? Rating { get; set; }
    public string? Summary { get; set; }
    public string? AirDate { get; set; }
    public int Weekday { get; set; }  // 1-7 (Monday-Sunday)

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
