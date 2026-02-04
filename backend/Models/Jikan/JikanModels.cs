using System.Text.Json.Serialization;

namespace backend.Models.Jikan;

/// <summary>
/// Jikan API response wrapper for top anime endpoint
/// </summary>
public class JikanTopAnimeResponse
{
    [JsonPropertyName("data")]
    public List<JikanAnimeInfo> Data { get; set; } = new();

    [JsonPropertyName("pagination")]
    public JikanPagination? Pagination { get; set; }
}

/// <summary>
/// Jikan anime information from MAL
/// </summary>
public class JikanAnimeInfo
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("title_japanese")]
    public string? TitleJapanese { get; set; }

    [JsonPropertyName("title_english")]
    public string? TitleEnglish { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("scored_by")]
    public int? ScoredBy { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("images")]
    public JikanImages? Images { get; set; }
}

/// <summary>
/// Jikan image URLs
/// </summary>
public class JikanImages
{
    [JsonPropertyName("jpg")]
    public JikanImageFormat? Jpg { get; set; }

    [JsonPropertyName("webp")]
    public JikanImageFormat? Webp { get; set; }
}

/// <summary>
/// Jikan image format URLs
/// </summary>
public class JikanImageFormat
{
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("small_image_url")]
    public string? SmallImageUrl { get; set; }

    [JsonPropertyName("large_image_url")]
    public string? LargeImageUrl { get; set; }
}

/// <summary>
/// Jikan pagination information
/// </summary>
public class JikanPagination
{
    [JsonPropertyName("last_visible_page")]
    public int LastVisiblePage { get; set; }

    [JsonPropertyName("has_next_page")]
    public bool HasNextPage { get; set; }
}
