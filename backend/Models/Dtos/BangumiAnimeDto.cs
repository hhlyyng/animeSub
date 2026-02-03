using System.Text.Json.Serialization;

namespace backend.Models.Dtos;

/// <summary>
/// DTO for Bangumi API anime item response
/// </summary>
public class BangumiAnimeDto
{
    /// <summary>
    /// Bangumi subject ID
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Original name (usually Japanese with kanji/kana)
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Chinese name
    /// </summary>
    [JsonPropertyName("name_cn")]
    public string? NameCn { get; set; }

    /// <summary>
    /// Summary/description in Chinese
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Rating information
    /// </summary>
    [JsonPropertyName("rating")]
    public BangumiRatingDto? Rating { get; set; }

    /// <summary>
    /// Image URLs
    /// </summary>
    [JsonPropertyName("images")]
    public BangumiImagesDto? Images { get; set; }

    /// <summary>
    /// Air date string
    /// </summary>
    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    /// <summary>
    /// Air weekday (1-7)
    /// </summary>
    [JsonPropertyName("air_weekday")]
    public int AirWeekday { get; set; }
}

/// <summary>
/// DTO for Bangumi rating information
/// </summary>
public class BangumiRatingDto
{
    /// <summary>
    /// Total number of ratings
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Rating score (0-10)
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>
    /// Rank among all anime
    /// </summary>
    [JsonPropertyName("rank")]
    public int? Rank { get; set; }
}

/// <summary>
/// DTO for Bangumi image URLs
/// </summary>
public class BangumiImagesDto
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }

    [JsonPropertyName("common")]
    public string? Common { get; set; }

    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("small")]
    public string? Small { get; set; }

    [JsonPropertyName("grid")]
    public string? Grid { get; set; }
}
